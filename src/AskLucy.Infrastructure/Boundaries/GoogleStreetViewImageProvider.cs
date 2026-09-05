using System.Globalization;
using System.Text.Json;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GoogleStreetViewImageProviderLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "No Street View coverage near ({Latitude}, {Longitude}): {Status}")]
    public static partial void NoCoverage(ILogger logger, double latitude, double longitude, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Street View fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// Fetches ground-level imagery from the Street View Static API, aimed from points near a site's
/// perimeter back toward it.
/// </summary>
/// <remarks>
/// <para>
/// Added because satellite imagery alone left a real ambiguity unresolved: whether a wall or
/// fence actually runs along a mapped edge, or the vision correction is reading a tree canopy that
/// spills past it. A ground-level view settles that the way an overhead one cannot.
/// </para>
/// <para>
/// <b>The metadata call is not optional.</b> The Street View Static API does not 404 when there is
/// no coverage near a location — it returns 200 with a generic gray "no imagery" placeholder tile,
/// indistinguishable from a real photo by content-type or status code alone. Silently handing that
/// to the vision analyzer as ground truth would be actively worse than not calling it at all. The
/// free <c>streetview/metadata</c> endpoint reports <c>status</c> honestly, and its <c>pano_id</c>
/// is passed to the image request so the two calls are guaranteed to agree on which panorama was
/// used — reusing the coordinates a second time relies on both calls snapping identically, which
/// is exactly the kind of assumption that only breaks in production.
/// </para>
/// <para>
/// Same never-throws contract as <see cref="ISatelliteImageProvider"/>: a viewpoint with no
/// coverage is simply absent from the result, never an exception.
/// </para>
/// </remarks>
internal sealed class GoogleStreetViewImageProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> options,
    ILogger<GoogleStreetViewImageProvider> logger) : IStreetViewImageProvider
{
    private const int ImageSizePixels = 640;
    private const int FieldOfViewDegrees = 90;
    private const int Pitch = 0;

    /// <summary>How far a viewpoint may snap to find a panorama. Wide enough to reach the nearest
    /// public road from a point on a park's perimeter; narrow enough that "coverage" doesn't mean
    /// a photo of somewhere else entirely.</summary>
    private const int SnapRadiusMeters = 60;

    public async Task<IReadOnlyList<StreetViewImage>> FetchAsync(
        IReadOnlyList<GeoPoint> viewpoints, GeoPoint lookAt, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || viewpoints.Count == 0)
        {
            return [];
        }

        // Not disposed: factory-owned handler lifetime, same reasoning as GoogleSatelliteImageProvider.
        var httpClient = httpClientFactory.CreateClient("GoogleStaticMaps");

        // Fetched concurrently: each viewpoint costs two sequential Google calls (metadata, then
        // the image), and this whole step sits ahead of the vision call's own 30s budget rather
        // than inside it. Run one at a time and a handful of viewpoints could add several seconds
        // of pure network latency to every boundary turn for no reason - these requests share
        // nothing and gain nothing from being serialized.
        var tasks = viewpoints.Select(viewpoint => FetchOneAsync(httpClient, apiKey, viewpoint, lookAt, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.Where(image => image is not null).Select(image => image!).ToList();
    }

    private async Task<StreetViewImage?> FetchOneAsync(
        HttpClient httpClient, string apiKey, GeoPoint viewpoint, GeoPoint lookAt, CancellationToken cancellationToken)
    {
        try
        {
            var location = $"{viewpoint.Latitude.ToString("R", CultureInfo.InvariantCulture)},{viewpoint.Longitude.ToString("R", CultureInfo.InvariantCulture)}";
            var metadataUrl = "streetview/metadata"
                + $"?location={location}&radius={SnapRadiusMeters}&source=outdoor&key={Uri.EscapeDataString(apiKey)}";

            using var metadataResponse = await httpClient.GetAsync(metadataUrl, cancellationToken);
            if (!metadataResponse.IsSuccessStatusCode)
            {
                GoogleStreetViewImageProviderLog.NoCoverage(logger, viewpoint.Latitude, viewpoint.Longitude, $"HTTP {(int)metadataResponse.StatusCode}");
                return null;
            }

            using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var metadataDocument = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
            var status = metadataDocument.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;

            if (!string.Equals(status, "OK", StringComparison.Ordinal))
            {
                GoogleStreetViewImageProviderLog.NoCoverage(logger, viewpoint.Latitude, viewpoint.Longitude, status ?? "(no status)");
                return null;
            }

            var panoId = metadataDocument.RootElement.TryGetProperty("pano_id", out var panoElement)
                ? panoElement.GetString()
                : null;
            if (string.IsNullOrEmpty(panoId))
            {
                GoogleStreetViewImageProviderLog.NoCoverage(logger, viewpoint.Latitude, viewpoint.Longitude, "OK status but no pano_id");
                return null;
            }

            // The panorama's own snapped location, not the requested viewpoint — heading is
            // computed from wherever the camera actually is, not from where we asked it to be.
            var snappedLatitude = viewpoint.Latitude;
            var snappedLongitude = viewpoint.Longitude;
            if (metadataDocument.RootElement.TryGetProperty("location", out var locationElement))
            {
                if (locationElement.TryGetProperty("lat", out var latElement))
                {
                    snappedLatitude = latElement.GetDouble();
                }

                if (locationElement.TryGetProperty("lng", out var lngElement))
                {
                    snappedLongitude = lngElement.GetDouble();
                }
            }

            var snapped = new GeoPoint(snappedLatitude, snappedLongitude);
            var heading = GeometryMath.BearingDegrees(snapped, lookAt);

            var imageUrl = "streetview"
                + $"?size={ImageSizePixels}x{ImageSizePixels}"
                + $"&pano={Uri.EscapeDataString(panoId)}"
                + $"&heading={heading.ToString("F0", CultureInfo.InvariantCulture)}"
                + $"&pitch={Pitch}&fov={FieldOfViewDegrees}"
                + $"&key={Uri.EscapeDataString(apiKey)}";

            using var imageResponse = await httpClient.GetAsync(imageUrl, cancellationToken);
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType;
            if (!imageResponse.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                GoogleStreetViewImageProviderLog.NoCoverage(logger, viewpoint.Latitude, viewpoint.Longitude, $"image request HTTP {(int)imageResponse.StatusCode}");
                return null;
            }

            var bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            return new StreetViewImage(bytes, contentType, snapped.Latitude, snapped.Longitude, heading);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            GoogleStreetViewImageProviderLog.FetchException(logger, ex, viewpoint.Latitude, viewpoint.Longitude);
            return null;
        }
    }
}
