using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Overpass;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Overpass;

internal static partial class OverpassFeatureQueryProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass feature query failed for ({Latitude}, {Longitude})")]
    public static partial void QueryFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/089-site-geometry-specialist contracts/overpass-feature-query.md — the shared OpenStreetMap
/// read path. Its transport is the one SPEC-042's boundary search established: the named
/// <c>"Overpass"</c> client and a staggered hedge across every configured endpoint, first answer
/// wins. Extracted rather than copied so a further consumer cannot drift from that hard-won
/// behaviour.
/// </summary>
internal sealed class OverpassFeatureQueryProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OverpassOptions> options,
    ILogger<OverpassFeatureQueryProvider> logger) : IOverpassFeatureQueryProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";

    /// <summary>
    /// Head start between endpoints. The public Overpass endpoints are free, shared and frequently
    /// saturated: the same query returned 504, then 200 in 3.3 s, then 200 in 1.7 s, seconds apart.
    /// Starting the next host as soon as the current one looks slow keeps the normal path to one
    /// request while a saturated entry point cannot cost the caller its answer.
    ///
    /// <para>
    /// Staggered rather than all at once, deliberately: these are donated servers, and cancelling a
    /// client request frees our socket, not their CPU. A half-second head start does not triple
    /// their load.
    /// </para>
    /// </summary>
    private static readonly TimeSpan HedgeDelay = TimeSpan.FromMilliseconds(500);

    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    private readonly OverpassOptions _options = options.Value;

    public async Task<IReadOnlyList<OverpassFeature>> QueryAsync(
        OverpassFeatureQuery query, CancellationToken cancellationToken = default)
    {
        var endpoints = Endpoints().ToList();

        using var hedge = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<IReadOnlyList<OverpassFeature>>>(endpoints.Count);
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
                // One endpoint answered: the rest are no longer wanted. Their requests are aborted
                // rather than left to burn a shared server's time and this turn's connection.
                await hedge.CancelAsync().ConfigureAwait(false);
                await ObserveLosersAsync(pending).ConfigureAwait(false);
                return finished.Result;
            }

            if (finished.IsCanceled)
            {
                // Only the caller's token can cancel an attempt before some other attempt has
                // answered (a success returns above), so this is the caller giving up.
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            failures.AddRange(finished.Exception!.Flatten().InnerExceptions);
        }

        // Nothing answered. Every endpoint's own failure was already logged by QueryOnceAsync;
        // this carries them back to the caller instead of collapsing them into one message
        // (constitution §2.VIII — the cause travels with the failure).
        throw new BoundaryProviderUnavailableException(
            $"Every Overpass endpoint was unavailable for ({query.Center.Latitude}, {query.Center.Longitude}).",
            new AggregateException(failures));
    }

    /// <summary>The primary endpoint first, then each mirror — started staggered by <see cref="HedgeDelay"/>.</summary>
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

    /// <summary>Starts one endpoint's attempt after its own head start, then runs the query.</summary>
    private async Task<IReadOnlyList<OverpassFeature>> QueryStaggeredAsync(
        string endpoint, TimeSpan delay, OverpassFeatureQuery query, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return await QueryOnceAsync(endpoint, query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the losing attempts to unwind so their cancellation is observed rather than left
    /// behind as unobserved task exceptions. They are expected to be cancelled — the answer has
    /// already been returned by another endpoint — so nothing here can change the outcome.
    /// </summary>
    private static async Task ObserveLosersAsync(IEnumerable<Task> losers)
    {
        try
        {
            await Task.WhenAll(losers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentional: every one of these belongs to a request nobody is waiting for any more.
            // A loser that had genuinely failed was already logged by QueryOnceAsync.
        }
    }

    private async Task<IReadOnlyList<OverpassFeature>> QueryOnceAsync(
        string baseUrl, OverpassFeatureQuery query, CancellationToken cancellationToken)
    {
        try
        {
            // Not `using`: IHttpClientFactory owns the handler's lifetime (SPEC-042's own finding).
            var httpClient = httpClientFactory.CreateClient("Overpass");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}interpreter")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", BuildQuery(query))]),
            };
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);

            // HTTP 200 with empty elements and a `remark` means the server timed out or ran out
            // of memory — not that the area has no such feature. Reported as an unavailable
            // provider so a caller never builds an "empty result" conclusion on it.
            if (!string.IsNullOrWhiteSpace(result?.Remark))
            {
                throw new BoundaryProviderUnavailableException($"Overpass: {result.Remark}");
            }

            return Interpret(result?.Elements ?? []);
        }
        catch (BoundaryProviderUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up, or another endpoint already answered and the hedge cancelled this
            // one. Neither is a failure of this endpoint, so it must not be logged or wrapped as
            // one. A client timeout is different: it cancels the request without cancelling this
            // token, so it falls through to the catch below.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            OverpassFeatureQueryProviderLog.QueryFailed(logger, ex, query.Center.Latitude, query.Center.Longitude);
            throw new BoundaryProviderUnavailableException(
                $"Overpass feature query unavailable for ({query.Center.Latitude}, {query.Center.Longitude}).", ex);
        }
    }

    /// <summary>Builds the Overpass QL for one query. Internal for tests.</summary>
    internal static string BuildQuery(OverpassFeatureQuery query)
    {
        var around = FormattableString.Invariant(
            $"(around:{query.RadiusMeters},{query.Center.Latitude},{query.Center.Longitude})");

        var statements = new List<string>();
        foreach (var filter in query.Filters)
        {
            if (filter.Values is null)
            {
                statements.Add($"  way{around}[\"{filter.Key}\"];");
                if (filter.IncludeRelations)
                {
                    statements.Add($"  relation{around}[\"type\"=\"multipolygon\"][\"{filter.Key}\"];");
                }

                continue;
            }

            foreach (var value in filter.Values)
            {
                statements.Add($"  way{around}[\"{filter.Key}\"=\"{value}\"];");
                if (filter.IncludeRelations)
                {
                    statements.Add($"  relation{around}[\"type\"=\"multipolygon\"][\"{filter.Key}\"=\"{value}\"];");
                }
            }
        }

        return $"[out:json][timeout:25];({Environment.NewLine}{string.Join(Environment.NewLine, statements)}{Environment.NewLine});out geom;";
    }

    /// <summary>Turns Overpass elements into features. Internal for tests.</summary>
    internal static IReadOnlyList<OverpassFeature> Interpret(IReadOnlyList<OverpassElement> elements)
    {
        var features = new List<OverpassFeature>();

        foreach (var element in elements)
        {
            switch (element.Type)
            {
                case "way" when element.Geometry is { Count: >= 2 } geometry:
                    features.Add(new OverpassFeature(
                        $"osm_way_{element.Id}",
                        "way",
                        element.Tags ?? EmptyTags,
                        [.. geometry.Select(point => new GeoPoint(point.Lat, point.Lon))]));
                    break;

                case "relation":
                    var segments = (element.Members ?? [])
                        .Where(member => member.Type == "way" && member.Role is "outer" or "" && member.Geometry is { Count: >= 2 })
                        .Select(member => (IReadOnlyList<GeoPoint>)[.. member.Geometry!.Select(point => new GeoPoint(point.Lat, point.Lon))])
                        .ToList();

                    var ring = OverpassGeometry.AssembleLargestOuterRing(segments);
                    if (ring is not null)
                    {
                        features.Add(new OverpassFeature(
                            $"osm_relation_{element.Id}",
                            "relation",
                            element.Tags ?? EmptyTags,
                            ring));
                    }

                    break;
            }
        }

        return features;
    }

    internal sealed record OverpassResponse(
        [property: JsonPropertyName("elements")] IReadOnlyList<OverpassElement>? Elements,
        [property: JsonPropertyName("remark")] string? Remark);

    internal sealed record OverpassElement(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string>? Tags,
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry,
        [property: JsonPropertyName("members")] IReadOnlyList<OverpassMember>? Members = null);

    internal sealed record OverpassMember(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry);

    internal sealed record OverpassGeometryPoint(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon);
}
