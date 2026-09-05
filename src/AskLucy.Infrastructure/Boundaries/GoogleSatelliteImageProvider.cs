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
/// viewer renders on. <b>Renders the roadmap layer, not satellite photography</b>, despite the
/// type's name (kept to avoid a wide rename; see the remarks below for why).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why roadmap, not satellite.</b> Four measured live boundary corrections against satellite
/// imagery — with and without a Street View ground-truth cross-check — all drifted the same
/// direction (west, 16-35 m) and all made the outline worse. Satellite asks the model to find an
/// invisible property line in a noisy real-world photograph: ambiguous where a fence sits in
/// shadow or under a tree, and there is no way to tell "the model is unsure" from "the model is
/// confidently wrong." Google's roadmap tiles draw the park as a single flat-colour polygon with a
/// hard, anti-aliased edge and its own name label — tracing that edge is ordinary image
/// segmentation, the same class of task any vision model is reliably good at, because the boundary
/// is not being inferred, it is already drawn. Confirmed by hand first: the same experiment run
/// against a general-purpose vision model on this exact park's roadmap tile reproduced the
/// polygon, notch included, closely enough to be usable.
/// </para>
/// <para>
/// <b>Reference frame.</b> Still Google in and Google out: the polygon this reads off is Google's
/// own rendering, on the same basemap the viewer draws on, so there is no vendor-to-vendor frame
/// mismatch to correct for.
/// </para>
/// <para>
/// <b>Resolution.</b> Framed on the requested extent at the closest zoom that still contains it,
/// then rendered at <c>scale=2</c> — 1280 px across for the same ground, so the label and every
/// corner of the polygon stay legible rather than being crushed into a corner of a wide frame.
/// </para>
/// <para>
/// <b>Encoding.</b> JPEG, not PNG — smaller upload, and a rendered map has none of the compression
/// artefacts that would matter for a photograph.
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

    /// <summary>Static Maps rejects a larger zoom than this.</summary>
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

            // CA1873: the "expensive" argument is Math.Round on a double, on a path that is about
            // to make an HTTP request — deferring it saves nothing. An IsEnabled guard does not
            // silence the rule either, because it cannot see through the [LoggerMessage] partial.
            var metersPerPixel = MetersPerPixel(center.Latitude, zoom);
#pragma warning disable CA1873
            GoogleSatelliteImageProviderLog.Framed(
                logger, center.Latitude, center.Longitude, radiusMeters, zoom,
                (int)(metersPerPixel * ImageSizePixels), Math.Round(metersPerPixel / 2, 3));
#pragma warning restore CA1873

            var url = "staticmap"
                + $"?center={center.Latitude.ToString("R", CultureInfo.InvariantCulture)},{center.Longitude.ToString("R", CultureInfo.InvariantCulture)}"
                + $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}"
                + $"&size={ImageSizePixels}x{ImageSizePixels}"
                + "&scale=2&maptype=roadmap&format=jpg"
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
