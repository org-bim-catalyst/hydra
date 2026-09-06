using System.Globalization;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GoogleRenderedFillBoundaryExtractorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendered-fill boundary fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendered-fill boundary extraction threw for ({Latitude}, {Longitude})")]
    public static partial void ExtractionFailed(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rendered-fill boundary for ({Latitude}, {Longitude}): no plausible {ForcedColorHex} outline found in the fetched frame")]
    public static partial void NoOutlineFound(ILogger logger, double latitude, double longitude, string forcedColorHex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rendered-fill boundary for ({Latitude}, {Longitude}): tiled fetch at zoom {Zoom} failed, falling back to a single tile")]
    public static partial void TiledFetchFailed(ILogger logger, double latitude, double longitude, int zoom);
}

/// <summary>
/// See <see cref="IRenderedFillBoundaryExtractor"/>'s remarks for why this exists and what it
/// replaces. Forces the requested Static Maps feature category's fill to a single, maximally
/// distinct colour (pure green, <c>0x00FF00</c> — nothing else this renders is a saturated green:
/// roads are blue-grey, water is blue), fetches that styled tile, thresholds for the forced colour,
/// and runs the result through the same <see cref="MaskContourVectorizer"/> pipeline the
/// drawn-outline diagnostic already uses.
/// </summary>
/// <remarks>
/// <para>
/// The rendered tile is never shown to any user — this is a server-side-only fetch purely for
/// geometry extraction, entirely separate from the Maps JavaScript API map the viewer renders
/// client-side. Forcing a feature's fill colour here has no visible effect on anything a user sees.
/// </para>
/// <para>
/// <b>Tiling (2026-09-06).</b> A single 1280x1280px (scale=2) frame is Static Maps' fixed ceiling
/// per request — at the ground coverage this module needs, that works out to roughly 0.27 m/px,
/// which is fine at normal zoom but visibly quantises a vertex's true position once a user zooms
/// the viewer in far enough that a handful of source pixels fill the screen. Rather than accept
/// that ceiling, this fetches a 2x2 grid of four tiles one zoom level higher (via
/// <see cref="StaticMapFraming.OffsetByPixels"/>, confirmed live to align edge-to-edge with zero
/// seam) and stitches them into one larger canvas covering the same ground at roughly double the
/// linear resolution — for free, since it's the same total number of Static Maps requests worth of
/// pixels, just arranged as four tiles instead of one. If the fit zoom is already at Static Maps'
/// ceiling, or if any of the four tile fetches fails, this falls back to the original single-tile
/// fetch at the fit zoom rather than losing the whole rendered-fill path over one flaky
/// sub-request or an already-maxed-out zoom level.
/// </para>
/// <para>
/// One live-confirmed caveat: each Static Maps tile carries Google's own mandatory attribution
/// watermark (a "Google" logotype and "Map data" text near one corner) that cannot be turned off
/// via any <c>style</c> parameter, unlike ordinary map labels. Stitching four tiles means four
/// copies of that watermark scattered through the combined frame instead of one at its edge. A live
/// test found this harmless — a watermark landing well inside the shape becomes an interior hole
/// that <see cref="MaskContourVectorizer"/>'s outer-contour tracing never sees, the same way any
/// other small enclosed hole does not affect the outer boundary — but a watermark landing exactly
/// on the site's true edge could, in principle, distort that one corner. Not something this module
/// can prevent (Google controls watermark placement, not this code), only note.
/// </para>
/// </remarks>
internal sealed class GoogleRenderedFillBoundaryExtractor(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> options,
    ILogger<GoogleRenderedFillBoundaryExtractor> logger) : IRenderedFillBoundaryExtractor
{
    /// <summary>Pure green — chosen because nothing else this renders comes close to it.</summary>
    private const string ForcedColorHex = "00FF00";

    /// <summary>Actual pixel size of one fetched tile at <c>scale=2</c>.</summary>
    private const int ScaledTileSize = StaticMapFraming.ImageSizePixels * 2;

    /// <summary>
    /// The four sub-tile centre offsets (scale-1 pixels) for a 2x2 grid one zoom level above the
    /// single-tile fit zoom — each offset by half a tile-width/-height from the overall centre, so
    /// adjacent tiles share an edge with zero gap or overlap (see <see cref="StaticMapFraming.OffsetByPixels"/>).
    /// Order: top-left, top-right, bottom-left, bottom-right — <see cref="StitchTiles"/> depends on this order.
    /// </summary>
    private static readonly (double Dx, double Dy)[] GridOffsets =
    [
        (-StaticMapFraming.ImageSizePixels / 2.0, -StaticMapFraming.ImageSizePixels / 2.0),
        (StaticMapFraming.ImageSizePixels / 2.0, -StaticMapFraming.ImageSizePixels / 2.0),
        (-StaticMapFraming.ImageSizePixels / 2.0, StaticMapFraming.ImageSizePixels / 2.0),
        (StaticMapFraming.ImageSizePixels / 2.0, StaticMapFraming.ImageSizePixels / 2.0),
    ];

    public async Task<IReadOnlyList<GeoPoint>?> TryExtractAsync(
        GeoPoint center, int radiusMeters, string googleMapsFeatureType, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            GoogleRenderedFillBoundaryExtractorLog.FetchFailed(logger, center.Latitude, center.Longitude, "no Google Maps API key configured");
            return null;
        }

        try
        {
            var fitZoom = StaticMapFraming.ChooseZoomToFit(center.Latitude, radiusMeters);

            (byte[] Bytes, SatelliteImage Bounds)? fetched = null;
            if (fitZoom < StaticMapFraming.MaxZoom)
            {
                fetched = await FetchStitchedTileAsync(center, fitZoom + 1, googleMapsFeatureType, apiKey, cancellationToken);
                if (fetched is null)
                {
                    GoogleRenderedFillBoundaryExtractorLog.TiledFetchFailed(logger, center.Latitude, center.Longitude, fitZoom + 1);
                }
            }

            fetched ??= await FetchSingleTileAsync(center, fitZoom, googleMapsFeatureType, apiKey, cancellationToken);

            if (fetched is not { } result)
            {
                GoogleRenderedFillBoundaryExtractorLog.FetchFailed(logger, center.Latitude, center.Longitude, "no image available from any fetch attempt");
                return null;
            }

            var ring = ExtractForcedColorRing(result.Bytes, result.Bounds);
            if (ring is null)
            {
                GoogleRenderedFillBoundaryExtractorLog.NoOutlineFound(logger, center.Latitude, center.Longitude, ForcedColorHex);
            }

            return ring;
        }
        // ImageFormatException (UnknownImageFormatException, InvalidImageContentException) covers
        // a malformed or unrecognisable response body — this class decodes the image itself, unlike
        // GoogleSatelliteImageProvider, which only ever hands raw bytes onward; a bad response here
        // must degrade to null exactly like an HTTP failure, never throw into the caller.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ImageFormatException)
        {
            GoogleRenderedFillBoundaryExtractorLog.ExtractionFailed(logger, ex, center.Latitude, center.Longitude);
            return null;
        }
    }

    private async Task<(byte[] Bytes, SatelliteImage Bounds)?> FetchSingleTileAsync(
        GeoPoint center, int zoom, string googleMapsFeatureType, string apiKey, CancellationToken cancellationToken)
    {
        var bytes = await FetchTileBytesAsync(center, zoom, googleMapsFeatureType, apiKey, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);
        return (bytes, new SatelliteImage(bytes, "image/jpeg", west, south, east, north));
    }

    /// <summary>
    /// Fetches the four-tile grid in parallel and stitches them into one larger canvas. Returns
    /// <see langword="null"/> on any partial failure — the caller falls back to a single tile at
    /// the original (one level lower) zoom rather than losing the rendered-fill path entirely over
    /// one flaky sub-request.
    /// </summary>
    private async Task<(byte[] Bytes, SatelliteImage Bounds)?> FetchStitchedTileAsync(
        GeoPoint overallCenter, int zoom, string googleMapsFeatureType, string apiKey, CancellationToken cancellationToken)
    {
        var tileCenters = GridOffsets
            .Select(o => StaticMapFraming.OffsetByPixels(overallCenter, zoom, o.Dx, o.Dy))
            .ToArray();

        var tileBytes = await Task.WhenAll(
            tileCenters.Select(c => FetchTileBytesAsync(c, zoom, googleMapsFeatureType, apiKey, cancellationToken)));

        if (Array.Exists(tileBytes, b => b is null))
        {
            return null;
        }

        using var canvas = StitchTiles(tileBytes!);

        var (west, _, _, north) = StaticMapFraming.CoveredBounds(tileCenters[0], zoom); // top-left
        var (_, south, east, _) = StaticMapFraming.CoveredBounds(tileCenters[3], zoom); // bottom-right

        using var stream = new MemoryStream();
        canvas.SaveAsJpeg(stream);
        var bytes = stream.ToArray();

        return (bytes, new SatelliteImage(bytes, "image/jpeg", west, south, east, north));
    }

    /// <summary>Lays four same-size tiles (top-left, top-right, bottom-left, bottom-right, matching <see cref="GridOffsets"/>'s order) into one 2x-width, 2x-height canvas.</summary>
    private static Image<Rgba32> StitchTiles(byte[][] tileBytes)
    {
        var canvas = new Image<Rgba32>(ScaledTileSize * 2, ScaledTileSize * 2);

        for (var i = 0; i < tileBytes.Length; i++)
        {
            using var tile = Image.Load<Rgba32>(tileBytes[i]);
            var offsetX = i is 1 or 3 ? ScaledTileSize : 0; // top-right, bottom-right
            var offsetY = i is 2 or 3 ? ScaledTileSize : 0; // bottom-left, bottom-right

            for (var y = 0; y < tile.Height; y++)
            {
                for (var x = 0; x < tile.Width; x++)
                {
                    canvas[offsetX + x, offsetY + y] = tile[x, y];
                }
            }
        }

        return canvas;
    }

    private async Task<byte[]?> FetchTileBytesAsync(
        GeoPoint tileCenter, int zoom, string googleMapsFeatureType, string apiKey, CancellationToken cancellationToken)
    {
        var fillStyle = $"feature:{googleMapsFeatureType}|element:geometry.fill|color:0x{ForcedColorHex}";
        // Confirmed live: every label and marker icon Google draws directly on the map (the
        // site's own name label + pin, a POI marker sitting inside it) punches a small non-green
        // hole through the fill exactly where it sits — indistinguishable, to a pixel threshold,
        // from a real gap in the boundary. This image is never shown to anyone and exists purely
        // for colour thresholding, so there is no reason to render any label at all.
        var noLabelsStyle = "feature:all|element:labels|visibility:off";
        // maptype=terrain, not roadmap: confirmed live that terrain simply does not render
        // building footprints at all, at any size — a building fully inside a site punched exactly
        // the same kind of hole through the fill as a label did on roadmap.
        var url = "staticmap"
            + $"?center={tileCenter.Latitude.ToString("R", CultureInfo.InvariantCulture)},{tileCenter.Longitude.ToString("R", CultureInfo.InvariantCulture)}"
            + $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}"
            + $"&size={StaticMapFraming.ImageSizePixels}x{StaticMapFraming.ImageSizePixels}"
            + "&scale=2&maptype=terrain&format=jpg"
            + $"&style={Uri.EscapeDataString(fillStyle)}"
            + $"&style={Uri.EscapeDataString(noLabelsStyle)}"
            + $"&key={Uri.EscapeDataString(apiKey)}";

        var httpClient = httpClientFactory.CreateClient("GoogleStaticMaps");
        using var response = await httpClient.GetAsync(url, cancellationToken);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
        {
            GoogleRenderedFillBoundaryExtractorLog.FetchFailed(
                logger, tileCenter.Latitude, tileCenter.Longitude, $"HTTP {(int)response.StatusCode}, content-type {contentType}");
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Thresholds tuned generously around pure green to absorb JPEG compression artefacts at the
    /// fill's edge, while nothing else this renders (roads, water, other POI fills) is remotely
    /// close to a saturated green — no ambiguity to resolve the way an AI-drawn red line's exact
    /// shade was never fully known in advance.
    /// </summary>
    private static IReadOnlyList<GeoPoint>? ExtractForcedColorRing(byte[] imageBytes, SatelliteImage bounds)
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
                    mask[x, y] = pixel.G >= 180 && pixel.R <= 120 && pixel.B <= 120;
                }
            }
        });

        return MaskContourVectorizer.TryExtractRing(mask, width, height, bounds);
    }
}
