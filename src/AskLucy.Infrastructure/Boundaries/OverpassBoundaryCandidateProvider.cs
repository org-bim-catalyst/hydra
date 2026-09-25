using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class OverpassBoundaryCandidateProviderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Overpass boundary search failed transiently (attempt {Attempt} of {MaxAttempts}); retrying")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass boundary search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/042-site-boundary-resolution research.md #2/#12 — OSM Overpass API-backed
/// implementation of <see cref="IBoundaryCandidateProvider"/>, mirroring
/// <c>NominatimGeocodingProvider</c>'s structure exactly (named <c>HttpClient</c>, typed
/// unavailable-exception, no caching by design).
///
/// Closed <c>way</c> elements and, for the curated-value tag filters, <c>multipolygon</c>
/// relations — the relation's outer ring is assembled from its member ways. Relations were
/// skipped in v1, which silently lost every site mapped that way: The Dubai Mall is relation
/// 18195959, so it never became a candidate and a nearby sliver was highlighted instead
/// (2026-09-25).
/// </summary>
internal sealed class OverpassBoundaryCandidateProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OverpassOptions> options,
    ILogger<OverpassBoundaryCandidateProvider> logger) : IBoundaryCandidateProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";

    /// <summary>
    /// OSM tag filters marking a way as a plausible site-boundary candidate — ported from the
    /// reference notebook's own curated <c>BOUNDARY_SEARCH_TAGS</c>, not a blanket "any value of
    /// this key" scan. <c>Values: null</c> means "any value" (matches the notebook's
    /// <c>landuse: True</c>/<c>tourism: True</c>/<c>natural: True</c>); a non-null list restricts
    /// to those exact values, exactly as the notebook restricts <c>leisure</c> to
    /// park/garden/nature_reserve/recreation_ground and <c>amenity</c> to institutional types.
    ///
    /// Corrected after a live production failure: an unrestricted <c>["leisure"]</c> filter also
    /// matched <c>leisure=playground</c>/<c>pitch</c>/<c>swimming_pool</c> — small sub-features
    /// *within* a park, not candidates for the park's own boundary — and one of them (closer to
    /// the geocoded center than the park's own way) won on proximity alone, since this codebase's
    /// deterministic name-matcher can't credit an OSM name tagged in a different script (e.g.
    /// Arabic "حديقة الصفا 2") against a Latin-script query ("Al Safa Park 2"). Restricting
    /// <c>leisure</c>/<c>amenity</c> to the notebook's own curated values excludes that entire
    /// class of wrong candidate at the source, rather than trying to out-score it after the fact.
    /// "building"/"boundary" stay excluded — see research.md #2 (query cost) and #7 (oversized
    /// administrative areas already penalized by geometry-quality) for why.
    ///
    /// <c>shop=mall</c> added 2026-09-25: malls are among the most-named sites in the city and
    /// match none of the other filters, so The Dubai Mall and BurJuman were never candidates and
    /// whatever landuse way lay nearest the pin was highlighted instead.
    /// </summary>
    private static readonly (string Key, string[]? Values)[] CandidateTagFilters =
    [
        ("leisure", ["park", "garden", "nature_reserve", "recreation_ground"]),
        ("landuse", null),
        ("amenity", ["school", "hospital", "university", "college"]),
        ("tourism", null),
        ("natural", null),
        ("shop", ["mall"]),
    ];

    private readonly OverpassOptions _options = options.Value;

    /// <summary>
    /// Attempts against Overpass before giving up, and the pause between them.
    ///
    /// <para>
    /// The public Overpass endpoint is a free, shared, frequently-saturated service: it answers
    /// in a second or two most of the time and returns 504 or 429 the rest, for the same query,
    /// seconds apart. Measured here on three consecutive identical requests — 504, then 200 in
    /// 3.3 s, then 200 in 1.7 s. Without a retry the first of those told the user "I couldn't
    /// look up the site boundary right now" for a boundary that was there all along.
    /// </para>
    ///
    /// <para>
    /// Three attempts at roughly a second apart stays well inside the 45 s budget
    /// <c>SendChatMessageCommandHandler</c> allows the whole boundary step, so a genuinely down
    /// Overpass still fails fast rather than holding the turn.
    /// </para>
    /// </summary>
    private const int MaxAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The primary endpoint first, then each mirror. Retries within a host are pointless when the
    /// host itself is saturated, which is the failure that actually happens: overpass-api.de
    /// answered 504 three times in a row, ~10 s apart, and the site was left with no boundary.
    /// </summary>
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

    public async Task<IReadOnlyList<BoundaryCandidate>> SearchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        var endpoints = Endpoints().ToList();
        for (var attempt = 1; ; attempt++)
        {
            // Each attempt moves to the next host, wrapping once the list is exhausted.
            var endpoint = endpoints[(attempt - 1) % endpoints.Count];
            try
            {
                return await SearchOnceAsync(endpoint, center, radiusMeters, cancellationToken);
            }
            catch (BoundaryProviderUnavailableException) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                OverpassBoundaryCandidateProviderLog.RetryingAfterTransientFailure(logger, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<BoundaryCandidate>> SearchOnceAsync(
        string baseUrl, GeoPoint center, int radiusMeters, CancellationToken cancellationToken)
    {
        try
        {
            // Not `using`: IHttpClientFactory owns the handler's lifetime, and disposing the
            // client here left the retry above reaching for a disposed one on its second
            // attempt — the retry could never have worked.
            var httpClient = httpClientFactory.CreateClient("Overpass");

            var query = BuildQuery(center, radiusMeters);
            var url = $"{baseUrl}interpreter";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]),
            };
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);
            var elements = result?.Elements ?? [];

            return elements
                .Select(e => (Element: e, Ring: OuterRingOf(e)))
                .Where(x => x.Ring is not null)
                .Select(x => MapElementToCandidate(x.Element, x.Ring!, center))
                .ToList();
        }
        catch (BoundaryProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            OverpassBoundaryCandidateProviderLog.SearchFailed(logger, ex, center.Latitude, center.Longitude);
            throw new BoundaryProviderUnavailableException(
                $"Overpass boundary search unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    /// <summary>
    /// Relations are queried only for the curated-value filters. An any-value relation query on
    /// <c>landuse</c>/<c>natural</c>/<c>tourism</c> pulls in district-scale landuse and whole
    /// water bodies — expensive to download and never the site a user named.
    /// </summary>
    private static string BuildQuery(GeoPoint center, int radiusMeters)
    {
        var around = $"(around:{radiusMeters},{center.Latitude},{center.Longitude})";
        var statements = CandidateTagFilters.SelectMany(filter =>
            filter.Values is null
                ? [$"  way{around}[\"{filter.Key}\"];"]
                : filter.Values.SelectMany(value => new[]
                {
                    $"  way{around}[\"{filter.Key}\"=\"{value}\"];",
                    $"  relation{around}[\"type\"=\"multipolygon\"][\"{filter.Key}\"=\"{value}\"];",
                }));

        var filters = string.Join(Environment.NewLine, statements);
        return $"[out:json][timeout:25];({Environment.NewLine}{filters}{Environment.NewLine});out geom;";
    }

    private static bool IsClosedRing(IReadOnlyList<OverpassGeometryPoint> geometry) =>
        SamePoint(geometry[0], geometry[^1]);

    // Member ways of a relation share their end nodes, so matching coordinates are exact copies.
    private static bool SamePoint(OverpassGeometryPoint a, OverpassGeometryPoint b) =>
        Math.Abs(a.Lat - b.Lat) < 1e-9 && Math.Abs(a.Lon - b.Lon) < 1e-9;

    /// <summary>
    /// The element's boundary as a closed ring, or <see langword="null"/> if it has none: a closed
    /// way's own geometry, or the largest outer ring a multipolygon relation's members close into.
    /// </summary>
    private static List<GeoPoint>? OuterRingOf(OverpassElement element)
    {
        var geometry = element.Type switch
        {
            "way" when element.Geometry is { Count: >= 4 } way && IsClosedRing(way) => way,
            "relation" => AssembleLargestOuterRing(element.Members ?? []),
            _ => null,
        };

        return geometry?.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList();
    }

    /// <summary>
    /// Joins a relation's outer member ways end to end into closed rings and returns the largest.
    /// OSM splits long outlines across several ways, in no particular order or direction, so each
    /// step takes whichever unused way starts or ends where the ring currently ends, reversing it
    /// if needed. A chain that cannot close is dropped rather than force-closed — a straight line
    /// across a gap would be an invented edge. Holes (inner members) are ignored: the candidate is
    /// the site's outline.
    /// </summary>
    private static List<OverpassGeometryPoint>? AssembleLargestOuterRing(IReadOnlyList<OverpassMember> members)
    {
        var segments = members
            .Where(m => m.Type == "way" && m.Role is "outer" or "" && m.Geometry is { Count: >= 2 })
            .Select(m => m.Geometry!.ToList())
            .ToList();

        List<OverpassGeometryPoint>? largest = null;
        var largestArea = 0.0;
        while (segments.Count > 0)
        {
            var ring = segments[0];
            segments.RemoveAt(0);

            while (!IsClosedRing(ring))
            {
                var nextIndex = segments.FindIndex(s => SamePoint(s[0], ring[^1]) || SamePoint(s[^1], ring[^1]));
                if (nextIndex < 0)
                {
                    break;
                }

                var next = segments[nextIndex];
                segments.RemoveAt(nextIndex);
                if (!SamePoint(next[0], ring[^1]))
                {
                    next.Reverse();
                }

                ring.AddRange(next.Skip(1));
            }

            if (ring.Count < 4 || !IsClosedRing(ring))
            {
                continue;
            }

            var area = GeometryMath.AreaSquareMeters(ring.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList());
            if (area > largestArea)
            {
                largest = ring;
                largestArea = area;
            }
        }

        return largest;
    }

    private static BoundaryCandidate MapElementToCandidate(OverpassElement element, List<GeoPoint> ring, GeoPoint center)
    {
        var polygon = new SiteBoundaryPolygon(ring);
        var tags = element.Tags ?? new Dictionary<string, string>();
        var centroid = GeometryMath.Centroid(ring);

        return new BoundaryCandidate(
            Id: $"osm_{element.Type}_{element.Id}",
            Polygon: polygon,
            Source: SiteBoundarySource.OsmBoundary,
            Name: tags.GetValueOrDefault("name") ?? tags.GetValueOrDefault("name:en") ?? string.Empty,
            Tags: tags,
            DistanceToCenterMeters: GeometryMath.DistanceMeters(centroid, center),
            AreaSquareMeters: GeometryMath.AreaSquareMeters(ring));
    }

    private sealed record OverpassResponse([property: JsonPropertyName("elements")] IReadOnlyList<OverpassElement>? Elements);

    private sealed record OverpassElement(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string>? Tags,
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry,
        [property: JsonPropertyName("members")] IReadOnlyList<OverpassMember>? Members = null);

    private sealed record OverpassMember(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry);

    private sealed record OverpassGeometryPoint(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon);
}
