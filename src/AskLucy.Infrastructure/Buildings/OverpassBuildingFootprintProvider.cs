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
    private const double MetresPerLevel = 3.0;
    private const double DefaultHeightMetres = 9.0; // research D5 — 9 m is three levels at the same 3 m rule.
    private const double SiteBuildingEdgeToleranceMetres = 25.0; // research D8

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
    /// FR-012, research D8 — the site-building rule: containment first (in Overpass's own return
    /// order), then nearest edge within 25 m, otherwise none.</summary>
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

        var siteBuildingIndex = FindSiteBuildingIndex(usable.Select(u => u.Ring).ToList(), center);

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

        return new BuildingFootprintResult(buildings, limited, excludedCount, radiusMetres);
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
            return (levels * MetresPerLevel, BuildingHeightProvenance.Assumed);
        }

        return (DefaultHeightMetres, BuildingHeightProvenance.Assumed);
    }

    /// <summary>FR-013 — excludes a footprint with fewer than 4 points (already filtered by the
    /// caller), that is not a closed ring, has zero/near-zero area, or is self-intersecting.</summary>
    private static bool IsUsableRing(IReadOnlyList<GeoPoint> ring)
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

    /// <summary>research D8 — the footprint whose polygon contains the site point (tested in
    /// Overpass's own return order, first containing footprint wins); failing that, the footprint
    /// whose edge is nearest to the site point, provided it is within 25 m; otherwise none (-1).</summary>
    private static int FindSiteBuildingIndex(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, GeoPoint sitePoint)
    {
        for (var i = 0; i < rings.Count; i++)
        {
            if (PointInPolygon(sitePoint, rings[i])) return i;
        }

        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < rings.Count; i++)
        {
            var distance = DistanceToRingEdge(sitePoint, rings[i]);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestDistance <= SiteBuildingEdgeToleranceMetres ? nearestIndex : -1;
    }

    private static bool PointInPolygon(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            var intersects = ((pi.Latitude > point.Latitude) != (pj.Latitude > point.Latitude)) &&
                              (point.Longitude < ((pj.Longitude - pi.Longitude) * (point.Latitude - pi.Latitude) / (pj.Latitude - pi.Latitude)) + pi.Longitude);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private static double DistanceToRingEdge(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        var reference = point;
        var (px, py) = GeometryMath.ToLocalMeters(point, reference); // (0, 0)
        var local = ring.Select(p => GeometryMath.ToLocalMeters(p, reference)).ToList();

        var minDistance = double.MaxValue;
        for (var i = 0; i < local.Count; i++)
        {
            var a = local[i];
            var b = local[(i + 1) % local.Count];
            var distance = DistancePointToSegment(px, py, a.X, a.Y, b.X, b.Y);
            if (distance < minDistance) minDistance = distance;
        }
        return minDistance;
    }

    private static double DistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        var t = lengthSquared < 1e-9 ? 0 : Math.Clamp((((px - ax) * abx) + ((py - ay) * aby)) / lengthSquared, 0, 1);
        var closestX = ax + (t * abx);
        var closestY = ay + (t * aby);
        var dx = px - closestX;
        var dy = py - closestY;
        return Math.Sqrt((dx * dx) + (dy * dy));
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
