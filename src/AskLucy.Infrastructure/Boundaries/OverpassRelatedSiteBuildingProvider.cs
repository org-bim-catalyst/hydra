using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class OverpassRelatedSiteBuildingProviderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Overpass related-building search failed transiently (attempt {Attempt} of {MaxAttempts}); retrying")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass related-building search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/077 — named buildings and stations around a site, from OpenStreetMap via Overpass.
/// <para>
/// Buildings are asked for twice, by <c>name</c> and by <c>name:en</c>: BurJuman's office tower
/// carries only <c>name:en</c>, so a <c>name</c>-only query never saw it. A metro station is a
/// point in OSM — BurJuman's is two, one per entrance — so each station point is mapped onto the
/// nearest transport building footprint, which is usually unnamed; points that land on the same
/// footprint become one station, and a point with no footprint near it is dropped (a point has no
/// outline to highlight).
/// </para>
/// </summary>
internal sealed class OverpassRelatedSiteBuildingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OverpassOptions> options,
    ILogger<OverpassRelatedSiteBuildingProvider> logger) : IRelatedSiteBuildingProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";

    /// <summary>Same budget and reasoning as <see cref="OverpassBoundaryCandidateProvider"/>'s retries.</summary>
    private const int MaxAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>How far a station point may sit from its building's outline: BurJuman's entrance point is 37 m from its mapped station hall.</summary>
    private const double StationFootprintReachMeters = 80.0;

    private static readonly HashSet<string> TransportBuildingValues = ["transportation", "train_station"];

    private static readonly string[] NameTagKeys = ["name:en", "name", "alt_name", "official_name", "short_name"];

    private readonly OverpassOptions _options = options.Value;

    public async Task<IReadOnlyList<RelatedSiteBuilding>> FindNamedBuildingsAsync(
        GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        var endpoints = Endpoints().ToList();
        for (var attempt = 1; ; attempt++)
        {
            var endpoint = endpoints[(attempt - 1) % endpoints.Count];
            try
            {
                return await SearchOnceAsync(endpoint, center, radiusMeters, cancellationToken);
            }
            catch (BoundaryProviderUnavailableException) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                OverpassRelatedSiteBuildingProviderLog.RetryingAfterTransientFailure(logger, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
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

    private async Task<IReadOnlyList<RelatedSiteBuilding>> SearchOnceAsync(
        string baseUrl, GeoPoint center, int radiusMeters, CancellationToken cancellationToken)
    {
        try
        {
            // Not `using`: IHttpClientFactory owns the handler's lifetime (see OverpassBoundaryCandidateProvider).
            var httpClient = httpClientFactory.CreateClient("Overpass");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}interpreter")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", BuildQuery(center, radiusMeters))]),
            };
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);
            return Interpret(result?.Elements ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            OverpassRelatedSiteBuildingProviderLog.SearchFailed(logger, ex, center.Latitude, center.Longitude);
            throw new BoundaryProviderUnavailableException(
                $"Overpass related-building search unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    internal static string BuildQuery(GeoPoint center, int radiusMeters)
    {
        var around = FormattableString.Invariant($"(around:{radiusMeters},{center.Latitude},{center.Longitude})");
        return "[out:json][timeout:25];(" +
               $"way{around}[\"building\"][\"name\"];" +
               $"way{around}[\"building\"][\"name:en\"];" +
               $"way{around}[\"building\"~\"^(transportation|train_station)$\"];" +
               $"node{around}[\"railway\"=\"station\"];" +
               ");out geom;";
    }

    /// <summary>Turns Overpass's elements into named buildings, stations mapped onto their footprints. Internal for tests.</summary>
    internal static IReadOnlyList<RelatedSiteBuilding> Interpret(IReadOnlyList<OverpassElement> elements)
    {
        var ways = elements
            .Where(e => e.Type == "way" && e.Geometry is { Count: >= 4 } g && SamePoint(g[0], g[^1]))
            .Select(e => (Element: e, Ring: (IReadOnlyList<GeoPoint>)[.. e.Geometry!.Select(p => new GeoPoint(p.Lat, p.Lon))]))
            .GroupBy(w => w.Element.Id)
            .Select(g => g.First())
            .ToList();

        var results = new Dictionary<long, RelatedSiteBuilding>();
        foreach (var (element, ring) in ways)
        {
            var names = NamesOf(element.Tags);
            if (names.Count == 0)
            {
                continue;
            }

            var kind = IsTransport(element.Tags) ? SiteBoundaryMemberKind.TransportStation : SiteBoundaryMemberKind.Building;
            results[element.Id] = new RelatedSiteBuilding($"osm_way_{element.Id}", names[0], names, kind, ring);
        }

        var transportWays = ways.Where(w => IsTransport(w.Element.Tags)).ToList();
        foreach (var node in elements.Where(e => e.Type == "node" && e.Lat is not null && e.Lon is not null))
        {
            var names = NamesOf(node.Tags);
            if (names.Count == 0)
            {
                continue;
            }

            var point = new GeoPoint(node.Lat!.Value, node.Lon!.Value);
            var footprint = transportWays
                .Select(w => (Way: w, Distance: GeometryMath.DistanceToRingMeters(point, w.Ring)))
                .Where(x => x.Distance <= StationFootprintReachMeters)
                .OrderBy(x => x.Distance)
                .Select(x => x.Way)
                .FirstOrDefault();
            if (footprint.Element is null)
            {
                continue;
            }

            var id = footprint.Element.Id;
            var mergedNames = results.TryGetValue(id, out var existing)
                ? [.. existing.Names, .. names.Where(n => !existing.Names.Contains(n, StringComparer.OrdinalIgnoreCase))]
                : names;
            var display = existing?.Name ?? StationDisplayName(names[0], node.Tags);
            results[id] = new RelatedSiteBuilding($"osm_way_{id}", display, mergedNames, SiteBoundaryMemberKind.TransportStation, footprint.Ring);
        }

        return [.. results.Values];
    }

    /// <summary>A station is mapped under the place it serves ("BurJuman"); saying what it is keeps it from reading as the site itself.</summary>
    private static string StationDisplayName(string name, IReadOnlyDictionary<string, string>? tags)
    {
        if (name.Contains("station", StringComparison.OrdinalIgnoreCase) || name.Contains("metro", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var isMetro = tags is not null && tags.TryGetValue("station", out var station) && station is "subway" or "light_rail";
        return isMetro ? $"{name} Metro Station" : $"{name} Station";
    }

    private static bool IsTransport(IReadOnlyDictionary<string, string>? tags) =>
        tags is not null &&
        ((tags.TryGetValue("building", out var building) && TransportBuildingValues.Contains(building)) ||
         (tags.TryGetValue("railway", out var railway) && railway == "station"));

    /// <summary>Every distinct name the element carries, English first — the first is the one shown.</summary>
    private static List<string> NamesOf(IReadOnlyDictionary<string, string>? tags) =>
        tags is null
            ? []
            : [.. NameTagKeys
                .Select(k => tags.TryGetValue(k, out var v) ? v.Trim() : null)
                .OfType<string>()
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool SamePoint(OverpassGeometryPoint a, OverpassGeometryPoint b) =>
        Math.Abs(a.Lat - b.Lat) < 1e-9 && Math.Abs(a.Lon - b.Lon) < 1e-9;

    private sealed record OverpassResponse([property: JsonPropertyName("elements")] IReadOnlyList<OverpassElement>? Elements);

    internal sealed record OverpassElement(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string>? Tags,
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry,
        [property: JsonPropertyName("lat")] double? Lat = null,
        [property: JsonPropertyName("lon")] double? Lon = null);

    internal sealed record OverpassGeometryPoint(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon);
}
