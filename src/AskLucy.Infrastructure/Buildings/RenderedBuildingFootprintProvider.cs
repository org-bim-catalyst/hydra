using System.Globalization;
using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class RenderedBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendered building-footprint fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);
}

/// <summary>
/// specs/053-rendered-building-footprints — retrieves building footprints from the map provider's
/// own rendered imagery instead of OpenStreetMap, reusing the exact styled-static-map technique
/// specs/042's <see cref="GoogleRenderedFillBoundaryExtractor"/> already proved: force one feature
/// class to a known colour against a blank background, fetch the tile server-side, threshold the
/// pixels, vectorise. The image is fetched in memory and never shown to any user (FR-002) — it is
/// a local variable in <see cref="ExtractFootprints"/> and nowhere else.
///
/// <para>
/// The style — verified live before this feature was specified (research D1) — is
/// <c>maptype=roadmap</c> (not <c>terrain</c>, which specs/042 chose specifically because it does
/// NOT render buildings) with <c>landscape.man_made.building</c> forced to magenta against an
/// all-white, label-free frame. Plain <c>landscape.man_made</c> (no <c>.building</c>) floods the
/// entire urban fabric and is unusable — recorded so nobody retries it.
/// </para>
///
/// <para>
/// Vectorisation reuses <see cref="MaskContourVectorizer.ExtractAllRings"/> (research D2) with
/// 4-connectivity (research D4 — 8-connectivity would merge diagonally-touching buildings into one
/// giant shadow-caster) and a minimum area converted from <see cref="RenderedFootprintOptions.MinimumAreaSquareMetres"/>
/// into pixels via the tile's own resolution (research D5). Heights are always the stated default,
/// marked <see cref="BuildingHeightProvenance.Assumed"/> — a rendered image carries no height data,
/// and matching a footprint to an OSM tag is a cross-source alignment problem this feature
/// deliberately does not attempt (research D8).
/// </para>
///
/// <para>
/// Caches its own result internally, exactly like <see cref="OverpassBuildingFootprintProvider"/>
/// — there is no external wrapper to inherit a cache from (research D9).
/// </para>
/// </summary>
internal sealed class RenderedBuildingFootprintProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RenderedFootprintOptions> renderedOptions,
    IOptions<BuildingRetrievalOptions> retrievalOptions,
    IOptions<GoogleMapsGeocodingOptions> geocodingOptions,
    IMemoryCache cache,
    ILogger<RenderedBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    /// <summary>research D5 — matches <c>OverpassBuildingFootprintProvider</c>'s own default
    /// exactly, so a building resolved from either source without a known height reads identically.</summary>
    private const double DefaultHeightMetres = 9.0;

    private readonly RenderedFootprintOptions _renderedOptions = renderedOptions.Value;
    private readonly BuildingRetrievalOptions _retrievalOptions = retrievalOptions.Value;

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(center, radiusMetres);
        if (cache.TryGetValue<BuildingFootprintResult>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var result = await SearchUncachedAsync(center, radiusMetres, cancellationToken);
        cache.Set(cacheKey, result, _renderedOptions.CacheTtl);
        return result;
    }

    /// <summary>Deliberately a different prefix than <c>OverpassBuildingFootprintProvider</c>'s own
    /// cache key — the two providers share one <see cref="IMemoryCache"/> instance, and colliding
    /// keys would let one source's cached result be silently returned for the other's request.</summary>
    private static string BuildCacheKey(GeoPoint center, int radiusMetres) =>
        $"solar-buildings-rendered:{center.Latitude.ToString("F5", CultureInfo.InvariantCulture)},{center.Longitude.ToString("F5", CultureInfo.InvariantCulture)}:{radiusMetres}";

    private async Task<BuildingFootprintResult> SearchUncachedAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        var apiKey = geocodingOptions.Value.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BuildingProviderUnavailableException("No Google Maps API key configured for rendered building-footprint retrieval.");
        }

        var zoom = _renderedOptions.Zoom;
        var imageBytes = await FetchTileBytesAsync(center, zoom, apiKey, cancellationToken);

        var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);
        var bounds = new SatelliteImage([], "image/png", west, south, east, north);

        return ExtractFootprints(imageBytes, bounds, center, radiusMetres, zoom);
    }

    private async Task<byte[]> FetchTileBytesAsync(GeoPoint center, int zoom, string apiKey, CancellationToken cancellationToken)
    {
        // Verified live (research D1): forces buildings to a known colour against an all-white,
        // label-free frame. `landscape.man_made.building` — NOT `landscape.man_made` alone, which
        // floods the entire urban fabric and is unusable for thresholding.
        var noLabelsStyle = "feature:all|element:labels|visibility:off";
        var whiteGeometryStyle = "feature:all|element:geometry|color:0xffffff";
        var buildingFillStyle = "feature:landscape.man_made.building|element:geometry|color:0xff00ff";

        var url = "staticmap"
            + $"?center={center.Latitude.ToString("R", CultureInfo.InvariantCulture)},{center.Longitude.ToString("R", CultureInfo.InvariantCulture)}"
            + $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}"
            + $"&size={StaticMapFraming.ImageSizePixels}x{StaticMapFraming.ImageSizePixels}"
            // roadmap, not terrain: specs/042 chose terrain because it does NOT render buildings —
            // this feature needs the opposite, for the same reason (research D1).
            + "&scale=2&maptype=roadmap&format=png"
            + $"&style={Uri.EscapeDataString(noLabelsStyle)}"
            + $"&style={Uri.EscapeDataString(whiteGeometryStyle)}"
            + $"&style={Uri.EscapeDataString(buildingFillStyle)}"
            + $"&key={Uri.EscapeDataString(apiKey)}";

        try
        {
            var httpClient = httpClientFactory.CreateClient("GoogleStaticMaps");
            using var response = await httpClient.GetAsync(url, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                RenderedBuildingFootprintProviderLog.FetchFailed(
                    logger, center.Latitude, center.Longitude, $"HTTP {(int)response.StatusCode}, content-type {contentType}");
                throw new BuildingProviderUnavailableException(
                    $"Rendered building-footprint tile unavailable for ({center.Latitude}, {center.Longitude}).");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (BuildingProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            RenderedBuildingFootprintProviderLog.FetchFailed(logger, center.Latitude, center.Longitude, ex.Message);
            throw new BuildingProviderUnavailableException(
                $"Rendered building-footprint tile unavailable for ({center.Latitude}, {center.Longitude}).", ex);
        }
    }

    /// <summary>
    /// FR-002 — the decoded image and mask are local variables only, here and nowhere else; never
    /// written to disk, returned, or logged. A valid tile with no building pixels returns an EMPTY
    /// result, not an exception — the distinction contracts/rendered-footprint-provider.md and the
    /// composite provider (specs/053 US2) both depend on: an exception means this source is broken,
    /// an empty result means this source genuinely has nothing (research D1/contract).
    /// </summary>
    private BuildingFootprintResult ExtractFootprints(byte[] imageBytes, SatelliteImage bounds, GeoPoint center, int radiusMetres, int zoom)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        var width = image.Width;
        var height = image.Height;

        var mask = new bool[width, height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    // Mirrors the boundary extractor's own green-test shape/tolerance (research D1)
                    // — wide enough to absorb edge antialiasing, tight enough to never admit white.
                    mask[x, y] = pixel.R >= 180 && pixel.B >= 180 && pixel.G <= 120;
                }
            }
        });

        // research D5 — MinimumAreaSquareMetres converted to a pixel count using this tile's own
        // resolution (scale=2 halves MetersPerPixel's scale-1 figure).
        var metresPerPixel = StaticMapFraming.MetersPerPixel(center.Latitude, zoom) / 2.0;
        var minimumPixelArea = (int)Math.Ceiling(_renderedOptions.MinimumAreaSquareMetres / (metresPerPixel * metresPerPixel));

        // 4-connectivity (research D4) — buildings, unlike a single site boundary, must not merge
        // just because they touch diagonally in the rendering.
        var extraction = MaskContourVectorizer.ExtractAllRings(
            mask, width, height, bounds, minimumPixelArea, _renderedOptions.SimplifyTolerancePixels, eightConnected: false);

        var usableRings = new List<IReadOnlyList<GeoPoint>>();
        var excludedCount = extraction.DegenerateCount;

        foreach (var extracted in extraction.Rings)
        {
            // research D6 — a tile-edge-clipped footprint is fabricated geometry (the shape beyond
            // the frame is genuinely unknown) and is excluded. A footprint merely extending past
            // the analysis RADIUS is real and is kept whole below — truncating it would cast a
            // shadow the real building does not.
            if (extracted.TouchesTileEdge)
            {
                excludedCount++;
                continue;
            }

            // "Entirely outside the radius" — every vertex farther than radiusMetres from centre.
            // A ring with at least one vertex inside (or on) the radius is kept whole (research D6).
            var openRing = extracted.Geo.Count > 1 &&
                extracted.Geo[0].Latitude == extracted.Geo[^1].Latitude && extracted.Geo[0].Longitude == extracted.Geo[^1].Longitude
                ? extracted.Geo.Take(extracted.Geo.Count - 1).ToList()
                : extracted.Geo;

            if (openRing.All(p => GeometryMath.DistanceMeters(p, center) > radiusMetres))
            {
                excludedCount++;
                continue;
            }

            usableRings.Add(extracted.Geo);
        }

        var siteBuildingIndex = SiteBuildingLocator.FindIndex(usableRings, center);

        var limited = usableRings.Count > _retrievalOptions.MaxBuildingCount;
        var bounded = limited ? usableRings.Take(_retrievalOptions.MaxBuildingCount).ToList() : usableRings;

        var tileKey = $"{center.Latitude.ToString("F5", CultureInfo.InvariantCulture)},{center.Longitude.ToString("F5", CultureInfo.InvariantCulture)}";
        var buildings = new List<BuildingFootprint>(bounded.Count);
        for (var i = 0; i < bounded.Count; i++)
        {
            buildings.Add(new BuildingFootprint(
                Id: $"rendered_{tileKey}_{i}",
                Ring: bounded[i],
                HeightMetres: DefaultHeightMetres,
                HeightProvenance: BuildingHeightProvenance.Assumed,
                Name: string.Empty,
                IsSiteBuilding: i == siteBuildingIndex));
        }

        return new BuildingFootprintResult(buildings, limited, excludedCount, radiusMetres, BuildingFootprintSource.Rendered);
    }
}
