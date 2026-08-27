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
    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass boundary search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/042-site-boundary-resolution research.md #2/#12 — OSM Overpass API-backed
/// implementation of <see cref="IBoundaryCandidateProvider"/>, mirroring
/// <c>NominatimGeocodingProvider</c>'s structure exactly (named <c>HttpClient</c>, typed
/// unavailable-exception, no caching by design).
///
/// Only <c>way</c> elements (simple closed polygons) are handled — <c>relation</c>
/// (multipolygon) elements are skipped for v1. This covers the large majority of real
/// leisure/landuse/building boundaries; a future revision can add relation support as an
/// additive change to <see cref="MapElementToCandidate"/> without touching the query or the
/// rest of the pipeline.
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
    /// </summary>
    private static readonly (string Key, string[]? Values)[] CandidateTagFilters =
    [
        ("leisure", ["park", "garden", "nature_reserve", "recreation_ground"]),
        ("landuse", null),
        ("amenity", ["school", "hospital", "university", "college"]),
        ("tourism", null),
        ("natural", null),
    ];

    private readonly OverpassOptions _options = options.Value;

    public async Task<IReadOnlyList<BoundaryCandidate>> SearchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("Overpass");

            var query = BuildQuery(center, radiusMeters);
            var url = $"{_options.SearchBaseUrl}interpreter";
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
                .Where(e => e.Type == "way" && e.Geometry is { Count: >= 4 } && IsClosedRing(e.Geometry))
                .Select(e => MapElementToCandidate(e, center))
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

    private static string BuildQuery(GeoPoint center, int radiusMeters)
    {
        var statements = CandidateTagFilters.SelectMany(filter =>
            filter.Values is null
                ? [$"  way(around:{radiusMeters},{center.Latitude},{center.Longitude})[\"{filter.Key}\"];"]
                : filter.Values.Select(value =>
                    $"  way(around:{radiusMeters},{center.Latitude},{center.Longitude})[\"{filter.Key}\"=\"{value}\"];"));

        var filters = string.Join(Environment.NewLine, statements);
        return $"[out:json][timeout:25];({Environment.NewLine}{filters}{Environment.NewLine});out geom;";
    }

    private static bool IsClosedRing(IReadOnlyList<OverpassGeometryPoint> geometry)
    {
        var first = geometry[0];
        var last = geometry[^1];
        return Math.Abs(first.Lat - last.Lat) < 1e-9 && Math.Abs(first.Lon - last.Lon) < 1e-9;
    }

    private static BoundaryCandidate MapElementToCandidate(OverpassElement element, GeoPoint center)
    {
        var ring = element.Geometry!.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList();
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
        [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassGeometryPoint>? Geometry);

    private sealed record OverpassGeometryPoint(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon);
}
