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
}

/// <summary>
/// See <see cref="IRenderedFillBoundaryExtractor"/>'s remarks for why this exists and what it
/// replaces. Forces the requested Static Maps feature category's fill to a single, maximally
/// distinct colour (pure green, <c>0x00FF00</c> — nothing else on a roadmap tile is a saturated
/// green: roads are blue-grey, water is blue, buildings are near-white), fetches that styled tile,
/// thresholds for the forced colour, and runs the result through the same
/// <see cref="MaskContourVectorizer"/> pipeline the drawn-outline diagnostic already uses.
/// </summary>
/// <remarks>
/// The rendered tile is never shown to any user — this is a server-side-only fetch purely for
/// geometry extraction, entirely separate from the Maps JavaScript API map the viewer renders
/// client-side. Forcing a feature's fill colour here has no visible effect on anything a user sees.
/// </remarks>
internal sealed class GoogleRenderedFillBoundaryExtractor(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> options,
    ILogger<GoogleRenderedFillBoundaryExtractor> logger) : IRenderedFillBoundaryExtractor
{
    /// <summary>Pure green — chosen because nothing else Static Maps' roadmap style renders comes close to it.</summary>
    private const string ForcedColorHex = "00FF00";

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
            var zoom = StaticMapFraming.ChooseZoomToFit(center.Latitude, radiusMeters);
            var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);

            var style = $"feature:{googleMapsFeatureType}|element:geometry.fill|color:0x{ForcedColorHex}";
            var url = "staticmap"
                + $"?center={center.Latitude.ToString("R", CultureInfo.InvariantCulture)},{center.Longitude.ToString("R", CultureInfo.InvariantCulture)}"
                + $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}"
                + $"&size={StaticMapFraming.ImageSizePixels}x{StaticMapFraming.ImageSizePixels}"
                + "&scale=2&maptype=roadmap&format=jpg"
                + $"&style={Uri.EscapeDataString(style)}"
                + $"&key={Uri.EscapeDataString(apiKey)}";

            var httpClient = httpClientFactory.CreateClient("GoogleStaticMaps");
            using var response = await httpClient.GetAsync(url, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                GoogleRenderedFillBoundaryExtractorLog.FetchFailed(
                    logger, center.Latitude, center.Longitude, $"HTTP {(int)response.StatusCode}, content-type {contentType}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var bounds = new SatelliteImage(bytes, contentType, west, south, east, north);

            var ring = ExtractForcedColorRing(bytes, bounds);
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

    /// <summary>
    /// Thresholds tuned generously around pure green to absorb JPEG compression artefacts at the
    /// fill's edge, while nothing else on a roadmap tile (roads, buildings, water, other POI fills)
    /// is remotely close to a saturated green — no ambiguity to resolve the way an AI-drawn red
    /// line's exact shade was never fully known in advance.
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
