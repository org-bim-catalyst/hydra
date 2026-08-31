using System.Globalization;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GoogleSatelliteImageProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Google satellite image fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google satellite image fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Google satellite image for ({Latitude}, {Longitude}): requested {RadiusMeters} m radius, chose zoom {Zoom} covering {SpanMeters} m at {MetersPerPixel} m/px")]
    public static partial void Framed(ILogger logger, double latitude, double longitude, int radiusMeters, int zoom, int spanMeters, double metersPerPixel);
}

/// <summary>
/// Fetches the vision cross-check's imagery from Google Static Maps — the same provider the
/// viewer renders on.
/// </summary>
/// <remarks>
/// <para>
/// This replaced an ESRI World Imagery provider, for two reasons that both showed up in a live
/// boundary.
/// </para>
/// <para>
/// <b>Reference frame.</b> Whatever the analyzer reads off this image is used to reposition a
/// mapped outline that is then drawn on Google's basemap. Read off a different vendor's imagery,
/// even a perfect trace lands wherever the two vendors disagree — which is not a correction, it
/// is a frame swap. Same provider in and out means the offset we measure is the offset we want.
/// </para>
/// <para>
/// <b>Resolution.</b> The old provider took a fixed search radius, so a 90 x 166 m park occupied a
/// small part of a 1 km frame and no fence was more than a pixel or two wide. Nothing could be
/// traced from that image however good the model. This one frames the requested extent and picks
/// the closest zoom that still contains it, then renders at <c>scale=2</c> so the returned image
/// is 1280 px across for the same ground — roughly 0.25 m/px at park scale, where a wall is
/// several pixels wide and a corner is unambiguous.
/// </para>
/// <para>
/// <b>Encoding.</b> JPEG, not PNG. The bytes are uploaded to the vision model inside the
/// request body, so their size is part of the vision budget: the same 1280 px frame is
/// 1.13 MB as PNG and 337 KB as JPEG — 1.51 MB versus 449 KB once base64-encoded. Requests
/// carrying the PNG were still in flight when the 30 s budget expired. Lossy compression
/// costs nothing that matters here; the model is looking for walls and tree lines, not
/// pixel-exact colour.
/// </para>
/// <para>
/// Keeps <see cref="ISatelliteImageProvider"/>'s never-throws contract: a failed fetch returns
/// <see langword="null"/> and the caller treats that as "vision unavailable this run".
/// </para>
/// </remarks>
internal sealed class GoogleSatelliteImageProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> options,
    ILogger<GoogleSatelliteImageProvider> logger) : ISatelliteImageProvider
{
    /// <summary>Ground coverage of the request, in Static Maps' scale-1 pixels. <c>scale=2</c> returns twice this many pixels for the same ground.</summary>
    private const int ImageSizePixels = 640;

    /// <summary>Static Maps rejects a larger zoom; satellite coverage thins out well before it anyway.</summary>
    private const int MaxZoom = 20;

    /// <summary>Below this the image is too coarse to be worth analysing at all.</summary>
    private const int MinZoom = 14;

    private const double EarthCircumferenceMeters = 40_075_016.686;

    public async Task<SatelliteImage?> FetchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Not an error: no key configured means this enhancement is simply off, exactly as an
            // unreachable endpoint would be.
            GoogleSatelliteImageProviderLog.FetchFailed(logger, center.Latitude, center.Longitude, "no Google Maps API key configured");
            return null;
        }

        try
        {
            var zoom = ChooseZoomToFit(center.Latitude, radiusMeters);
            var (west, south, east, north) = CoveredBounds(center, zoom);

            // Framed is Debug-only and the framing maths exists solely to feed it, so the whole
            // block stays behind IsEnabled rather than costing a trig call per fetch in
            // Production, where Debug is off (CA1873).
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var metersPerPixel = MetersPerPixel(center.Latitude, zoom);
                GoogleSatelliteImageProviderLog.Framed(
                    logger, center.Latitude, center.Longitude, radiusMeters, zoom,
                    (int)(metersPerPixel * ImageSizePixels), Math.Round(metersPerPixel / 2, 3));
            }

            var url = "staticmap"
                + $"?center={center.Latitude.ToString("R", CultureInfo.InvariantCulture)},{center.Longitude.ToString("R", CultureInfo.InvariantCulture)}"
                + $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}"
                + $"&size={ImageSizePixels}x{ImageSizePixels}"
                + "&scale=2&maptype=satellite&format=jpg"
                + $"&key={Uri.EscapeDataString(apiKey)}";

            // Not disposed: the client comes from the factory, whose handler lifetime it manages.
            // Disposing it here breaks any later call that reuses the same handler.
            var httpClient = httpClientFactory.CreateClient("GoogleStaticMaps");
            using var response = await httpClient.GetAsync(url, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                GoogleSatelliteImageProviderLog.FetchFailed(
                    logger, center.Latitude, center.Longitude, $"HTTP {(int)response.StatusCode}, content-type {contentType}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new SatelliteImage(bytes, contentType, west, south, east, north);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            GoogleSatelliteImageProviderLog.FetchException(logger, ex, center.Latitude, center.Longitude);
            return null;
        }
    }

    /// <summary>
    /// The largest zoom whose frame still contains <paramref name="radiusMeters"/> in every
    /// direction — "zoom to fit", so the site fills as much of the image as it can without being
    /// clipped.
    /// </summary>
    /// <remarks>
    /// Static Maps only accepts integer zoom, so the chosen level almost always covers somewhat
    /// more ground than asked for. Erring outwards is deliberate: a clipped site loses the very
    /// corners the analyzer is meant to be reading.
    /// </remarks>
    private static int ChooseZoomToFit(double latitude, int radiusMeters)
    {
        var requiredMetersPerPixel = Math.Max(radiusMeters, 1) * 2.0 / ImageSizePixels;
        var scale = EarthCircumferenceMeters * Math.Abs(Math.Cos(latitude * Math.PI / 180.0)) / 256.0;
        var zoom = (int)Math.Floor(Math.Log2(scale / requiredMetersPerPixel));
        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    private static double MetersPerPixel(double latitude, int zoom) =>
        EarthCircumferenceMeters * Math.Abs(Math.Cos(latitude * Math.PI / 180.0)) / (256.0 * Math.Pow(2, zoom));

    /// <summary>
    /// The exact ground rectangle the returned image covers, in Web Mercator — the projection
    /// Static Maps renders in.
    /// </summary>
    /// <remarks>
    /// Computed rather than assumed, because the analyzer maps the model's normalised [0,1] pixel
    /// coordinates back to latitude/longitude by interpolating linearly between these edges. Over
    /// a few hundred metres Mercator's latitude curvature costs well under a metre; over a frame
    /// sized from a guess, being wrong about the edges costs the whole correction.
    /// </remarks>
    private static (double West, double South, double East, double North) CoveredBounds(GeoPoint center, int zoom)
    {
        var worldSize = 256.0 * Math.Pow(2, zoom);
        var half = ImageSizePixels / 2.0;

        var centerX = (center.Longitude + 180.0) / 360.0 * worldSize;
        var centerY = MercatorY(center.Latitude) * worldSize;

        var west = ((centerX - half) / worldSize * 360.0) - 180.0;
        var east = ((centerX + half) / worldSize * 360.0) - 180.0;
        var north = InverseMercatorY((centerY - half) / worldSize);
        var south = InverseMercatorY((centerY + half) / worldSize);

        return (west, south, east, north);
    }

    private static double MercatorY(double latitude)
    {
        var sin = Math.Sin(latitude * Math.PI / 180.0);
        return 0.5 - (Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI));
    }

    private static double InverseMercatorY(double y)
    {
        var n = Math.PI - (2.0 * Math.PI * y);
        return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
    }
}
