using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class OverpassBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Overpass building search failed transiently (attempt {Attempt} of {MaxAttempts}); retrying")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overpass building search failed for ({Latitude}, {Longitude})")]
    public static partial void SearchFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/052-solar-analysis research D4 — OSM Overpass-backed implementation of
/// <see cref="IBuildingFootprintProvider"/>, mirroring <c>OverpassBoundaryCandidateProvider</c>'s
/// structure exactly: the same named <c>HttpClient</c> ("Overpass"), the same mirror-rotation
/// retry policy (reusing <see cref="OverpassOptions"/> directly rather than duplicating its
/// hard-won mirror list), and a typed unavailable-exception. Only <c>way</c> elements are handled
/// — <c>relation</c> (multipolygon) elements are skipped for v1, exactly as the boundary provider
/// does and for the same reason (simple closed ways cover the large majority).
/// </summary>
internal sealed class OverpassBuildingFootprintProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OverpassOptions> overpassOptions,
    IOptions<BuildingRetrievalOptions> retrievalOptions,
    IMemoryCache cache,
    ILogger<OverpassBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";

    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly OverpassOptions _overpassOptions = overpassOptions.Value;
    private readonly BuildingRetrievalOptions _retrievalOptions = retrievalOptions.Value;

    private IEnumerable<string> Endpoints()
    {
        yield return _overpassOptions.SearchBaseUrl;
        foreach (var mirror in _overpassOptions.MirrorBaseUrls ?? [])
        {
            if (!string.IsNullOrWhiteSpace(mirror) && mirror != _overpassOptions.SearchBaseUrl)
            {
                yield return mirror;
            }
        }
    }

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(center, radiusMetres);
        if (cache.TryGetValue<BuildingFootprintResult>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var result = await SearchUncachedAsync(center, radiusMetres, cancellationToken);
        cache.Set(cacheKey, result, _retrievalOptions.CacheTtl);
        return result;
    }

    /// <summary>research D4 — keyed by rounded coordinates plus radius, so a returning user at
    /// (near enough) the same site and radius hits the cache rather than re-querying the flakiest
    /// external dependency in the system.</summary>
    private static string BuildCacheKey(GeoPoint center, int radiusMetres) =>
        $"solar-buildings:{center.Latitude.ToString("F5", CultureInfo.InvariantCulture)},{center.Longitude.ToString("F5", CultureInfo.InvariantCulture)}:{radiusMetres}";

    private async Task<BuildingFootprintResult> SearchUncachedAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        var endpoints = Endpoints().ToList();
        for (var attempt = 1; ; attempt++)
        {
            var endpoint = endpoints[(attempt - 1) % endpoints.Count];
            try
            {
                return await SearchOnceAsync(endpoint, center, radiusMetres, cancellationToken);
            }
            catch (BuildingProviderUnavailableException) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                OverpassBuildingFootprintProviderLog.RetryingAfterTransientFailure(logger, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private async Task<BuildingFootprintResult> SearchOnceAsync(string baseUrl, GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        try
        {
            // Not `using`: IHttpClientFactory owns the handler's lifetime (same reasoning as
            // OverpassBoundaryCandidateProvider — disposing here would break the retry above).
            var httpClient = httpClientFactory.CreateClient("Overpass");

            var query = BuildQuery(center, radiusMetres);
            var url = $"{baseUrl}interpreter";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]),
            };
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);
            var elements = parsed?.Elements ?? [];

            return BuildResult(elements, center, radiusMetres);
        }
        catch (BuildingProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            OverpassBuildingFootprintProviderLog.SearchFailed(logger, ex, center.Latitude, center.Longitude);
            throw new BuildingProviderUnavailableException(
                $"Overpass building search unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    private static string BuildQuery(GeoPoint center, int radiusMetres) =>
        $"[out:json][timeout:25];(way[\"building\"](around:{radiusMetres},{center.Latitude},{center.Longitude}););out geom;";

    /// <summary>FR-013 — unusable footprints are excluded and counted, never failing the whole
    /// analysis. FR-015 — the count cap sets <see cref="BuildingFootprintResult.Limited"/>.
    /// FR-012, research D8 — the site-building rule is <see cref="SiteBuildingLocator"/>, shared
    /// with specs/053's rendered provider so both sources apply the identical rule.</summary>
    private BuildingFootprintResult BuildResult(IReadOnlyList<OverpassElement> elements, GeoPoint center, int radiusMetres)
    {
        var usable = new List<(OverpassElement Element, IReadOnlyList<GeoPoint> Ring)>();
        var excludedCount = 0;

        foreach (var element in elements)
        {
            if (element.Type != "way" || element.Geometry is not { Count: >= 4 })
            {
                excludedCount++;
                continue;
            }

            var ring = element.Geometry.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList();
            if (!IsUsableRing(ring))
            {
                excludedCount++;
                continue;
            }

            usable.Add((element, ring));
        }

        var siteBuildingIndex = SiteBuildingLocator.FindIndex(usable.Select(u => u.Ring).ToList(), center);

        var limited = usable.Count > _retrievalOptions.MaxBuildingCount;
        var bounded = limited ? usable.Take(_retrievalOptions.MaxBuildingCount).ToList() : usable;

        var buildings = new List<BuildingFootprint>(bounded.Count);
        for (var i = 0; i < bounded.Count; i++)
        {
            var (element, ring) = bounded[i];
            var (heightMetres, provenance) = ResolveHeight(element.Tags);
            var tags = element.Tags ?? new Dictionary<string, string>();

            buildings.Add(new BuildingFootprint(
                Id: $"osm_way_{element.Id}",
                Ring: ring,
                HeightMetres: heightMetres,
                HeightProvenance: provenance,
                Name: tags.GetValueOrDefault("name") ?? tags.GetValueOrDefault("name:en") ?? string.Empty,
                IsSiteBuilding: i == siteBuildingIndex));
        }

        return new BuildingFootprintResult(buildings, limited, excludedCount, radiusMetres, BuildingFootprintSource.Osm);
    }

    /// <summary>research D5 — height tag (parsed leniently for a trailing "m") -&gt; known;
    /// building:levels × 3.0 m -&gt; assumed; default 9.0 m -&gt; assumed.</summary>
    private static (double HeightMetres, BuildingHeightProvenance Provenance) ResolveHeight(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is not null && tags.TryGetValue("height", out var heightTag))
        {
            var trimmed = heightTag.Trim();
            if (trimmed.EndsWith('m')) trimmed = trimmed[..^1].Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeight) && parsedHeight > 0)
            {
                return (parsedHeight, BuildingHeightProvenance.Known);
            }
        }

        if (tags is not null && tags.TryGetValue("building:levels", out var levelsTag) &&
            double.TryParse(levelsTag.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var levels) && levels > 0)
        {
            return (levels * AssumedBuildingHeight.MetresPerStorey, BuildingHeightProvenance.Assumed);
        }

        return (AssumedBuildingHeight.DefaultMetres, BuildingHeightProvenance.Assumed);
    }

    /// <summary>FR-013 — excludes a footprint with fewer than 4 points (already filtered by the
    /// caller), that is not a closed ring, has zero/near-zero area, or is self-intersecting.</summary>
    private static bool IsUsableRing(List<GeoPoint> ring)
    {
        var first = ring[0];
        var last = ring[^1];
        var isClosed = Math.Abs(first.Latitude - last.Latitude) < 1e-9 && Math.Abs(first.Longitude - last.Longitude) < 1e-9;
        if (!isClosed) return false;

        // Drop the duplicated closing vertex for area/self-intersection purposes.
        var openRing = ring.Take(ring.Count - 1).ToList();
        if (openRing.Count < 3) return false;

        var area = GeometryMath.AreaSquareMeters(openRing);
        if (area < 0.01) return false; // near-zero area

        return !IsSelfIntersecting(openRing);
    }

    /// <summary>Cheap segment-pair intersection over the ring's local-metres projection — at the
    /// bounded ring sizes OSM produces this is not worth a dedicated geometry library (research D7).</summary>
    private static bool IsSelfIntersecting(IReadOnlyList<GeoPoint> openRing)
    {
        var reference = GeometryMath.Centroid(openRing);
        var local = openRing.Select(p => GeometryMath.ToLocalMeters(p, reference)).ToList();
        var n = local.Count;

        for (var i = 0; i < n; i++)
        {
            var (a1, a2) = (local[i], local[(i + 1) % n]);
            for (var j = i + 1; j < n; j++)
            {
                // Skip edges that share a vertex with edge i — adjacent edges always "touch" at
                // the shared vertex, which is not a self-intersection.
                if (j == i || (j + 1) % n == i) continue;
                var (b1, b2) = (local[j], local[(j + 1) % n]);
                if (SegmentsIntersect(a1, a2, b1, b2)) return true;
            }
        }

        return false;
    }

    private static bool SegmentsIntersect((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3, (double X, double Y) p4)
    {
        double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);
        double d1 = Cross(p4.X - p3.X, p4.Y - p3.Y, p1.X - p3.X, p1.Y - p3.Y);
        double d2 = Cross(p4.X - p3.X, p4.Y - p3.Y, p2.X - p3.X, p2.Y - p3.Y);
        double d3 = Cross(p2.X - p1.X, p2.Y - p1.Y, p3.X - p1.X, p3.Y - p1.Y);
        double d4 = Cross(p2.X - p1.X, p2.Y - p1.Y, p4.X - p1.X, p4.Y - p1.Y);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
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
