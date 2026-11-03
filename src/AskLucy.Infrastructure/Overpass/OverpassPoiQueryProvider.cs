using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Overpass;

internal static partial class OverpassPoiQueryProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass POI query failed for ({Latitude}, {Longitude})")]
    public static partial void QueryFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/090-poi-discovery — reads points of interest (nodes first, then ways and relations by
/// their centre) from OpenStreetMap's Overpass API. Its transport is the same staggered hedge
/// across every configured endpoint that the boundary and feature queries already use: the public
/// instances are free, shared and frequently saturated, and retrying one saturated entry point is
/// not a strategy.
/// </summary>
internal sealed class OverpassPoiQueryProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OverpassOptions> options,
    ILogger<OverpassPoiQueryProvider> logger) : IPoiQueryProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";
    private static readonly TimeSpan HedgeDelay = TimeSpan.FromMilliseconds(500);
    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    private readonly OverpassOptions _options = options.Value;

    public async Task<IReadOnlyList<PoiFeature>> QueryAsync(
        PoiQuery query, CancellationToken cancellationToken = default)
    {
        var endpoints = Endpoints().ToList();

        using var hedge = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<IReadOnlyList<PoiFeature>>>(endpoints.Count);
        for (var i = 0; i < endpoints.Count; i++)
        {
            pending.Add(QueryStaggeredAsync(endpoints[i], HedgeDelay * i, query, hedge.Token));
        }

        var failures = new List<Exception>();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);

            if (finished.IsCompletedSuccessfully)
            {
                await hedge.CancelAsync().ConfigureAwait(false);
                await ObserveLosersAsync(pending).ConfigureAwait(false);
                return finished.Result;
            }

            if (finished.IsCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            failures.AddRange(finished.Exception!.Flatten().InnerExceptions);
        }

        throw new BoundaryProviderUnavailableException(
            $"Every Overpass endpoint was unavailable for ({query.Center.Latitude}, {query.Center.Longitude}).",
            new AggregateException(failures));
    }

    private IEnumerable<string> Endpoints()
    {
        yield return _options.SearchBaseUrl;
        foreach (var mirror in _options.MirrorBaseUrls ?? [])
        {
            if (!string.IsNullOrWhiteSpace(mirror) && mirror != _options.SearchBaseUrl)
            {
                yield return mirror;
            }
        }
    }

    private async Task<IReadOnlyList<PoiFeature>> QueryStaggeredAsync(
        string endpoint, TimeSpan delay, PoiQuery query, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return await QueryOnceAsync(endpoint, query, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ObserveLosersAsync(IEnumerable<Task> losers)
    {
        try
        {
            await Task.WhenAll(losers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Every loser belongs to a request nobody is waiting for any more; a genuine failure
            // was already logged by QueryOnceAsync.
        }
    }

    private async Task<IReadOnlyList<PoiFeature>> QueryOnceAsync(
        string baseUrl, PoiQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient("Overpass");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}interpreter")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", BuildQuery(query))]),
            };
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OverpassPoiResponse>(cancellationToken);
            return Interpret(result?.Elements ?? [], query.MaxResults);
        }
        catch (BoundaryProviderUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            OverpassPoiQueryProviderLog.QueryFailed(logger, ex, query.Center.Latitude, query.Center.Longitude);
            throw new BoundaryProviderUnavailableException(
                $"Overpass POI query unavailable for ({query.Center.Latitude}, {query.Center.Longitude}).", ex);
        }
    }

    /// <summary>Builds the Overpass QL for one POI search. Internal for tests.</summary>
    internal static string BuildQuery(PoiQuery query)
    {
        var around = FormattableString.Invariant(
            $"(around:{query.RadiusMeters},{query.Center.Latitude},{query.Center.Longitude})");

        var statements = new List<string>();
        foreach (var group in query.Category.TagGroups)
        {
            var constraints = string.Concat(group.Constraints.Select(FormatConstraint));

            statements.Add($"  node{constraints}{around};");
            statements.Add($"  way{constraints}{around};");
            statements.Add($"  relation{constraints}{around};");
        }

        return $"[out:json][timeout:25];({Environment.NewLine}{string.Join(Environment.NewLine, statements)}{Environment.NewLine});out center {query.MaxResults};";
    }

    private static string FormatConstraint(PoiTagConstraint constraint) =>
        constraint.Values is null || constraint.Values.Count == 0
            ? $"[\"{constraint.Key}\"]"
            : string.Concat(constraint.Values.Select(v => $"[\"{constraint.Key}\"=\"{v}\"]"));

    /// <summary>Turns Overpass elements into POI features. Internal for tests.</summary>
    internal static IReadOnlyList<PoiFeature> Interpret(
        IReadOnlyList<OverpassPoiElement> elements, int maxResults)
    {
        var features = new List<PoiFeature>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            if (features.Count >= maxResults)
            {
                break;
            }

            var id = element.Type switch
            {
                "node" => $"osm_node_{element.Id}",
                "way" => $"osm_way_{element.Id}",
                "relation" => $"osm_relation_{element.Id}",
                _ => null,
            };

            if (id is null || !seen.Add(id))
            {
                continue;
            }

            var latitude = element.Type == "node" ? element.Lat : element.Center?.Lat;
            var longitude = element.Type == "node" ? element.Lon : element.Center?.Lon;
            if (latitude is null || longitude is null)
            {
                continue;
            }

            var tags = element.Tags ?? EmptyTags;
            features.Add(new PoiFeature(
                id,
                NameOf(tags),
                latitude.Value,
                longitude.Value,
                tags));
        }

        return features;
    }

    private static string NameOf(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var key in new[] { "name:en", "name", "name:ar", "brand", "operator" })
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    internal sealed record OverpassPoiResponse(
        [property: JsonPropertyName("elements")] IReadOnlyList<OverpassPoiElement>? Elements);

    internal sealed record OverpassPoiElement(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("lat")] double? Lat,
        [property: JsonPropertyName("lon")] double? Lon,
        [property: JsonPropertyName("center")] OverpassPoiCenter? Center,
        [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string>? Tags);

    internal sealed record OverpassPoiCenter(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon);
}
