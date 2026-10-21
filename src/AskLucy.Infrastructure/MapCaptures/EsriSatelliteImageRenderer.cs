using System.Globalization;
using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.MapCaptures;

internal static partial class EsriSatelliteImageRendererLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "ESRI World Imagery fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ESRI World Imagery fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Building footprint lookup failed for ({Latitude}, {Longitude}); the capture is refused rather than silently returned without the overlay")]
    public static partial void FootprintLookupFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/083-viewer-map-capture T020 — the satellite-only fallback for deployments with no
/// Google Maps key. It renders the same rectangle and the same 1280x1280 frame as the Google
/// renderer, so the cropped output is identical in shape.
/// </summary>
public sealed class EsriSatelliteImageRenderer(
    IHttpClientFactory httpClientFactory,
    IOptions<EsriOptions> esriOptions,
    IBuildingFootprintProvider footprintProvider,
    MapImageComposer composer,
    ILogger<EsriSatelliteImageRenderer> logger) : IMapImageRenderer
{
    private const string ClientName = "EsriWorldImagery";
    private const string KeylessExportUrl =
        "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export";
    private const string AuthenticatedExportUrl =
        "https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/export";
    private const int ImageSizePixels = 1280;

    public async Task<MapImageResult> RenderAsync(
        MapImageRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Mode != MapCaptureMode.Satellite)
        {
            return MapImageResult.Failure(
                MapCaptureFailureReason.ModeUnavailable,
                $"Mode '{WireNameOf(request.Mode)}' is not available on the ESRI fallback; only 'satellite' is.");
        }

        var radiusMetres = Math.Max(request.ViewSizeMetres / 2, 1);
        var zoom = StaticMapFraming.ChooseZoomToFit(request.Latitude, radiusMetres);
        var center = new GeoPoint(request.Latitude, request.Longitude);
        var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);

        byte[] bytes;
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get, BuildUrl(west, south, east, north));

            var apiKey = esriOptions.Value.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-Esri-Authorization", $"Bearer {apiKey}");
            }

            using var response = await client.SendAsync(httpRequest, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode
                || contentType is null
                || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                EsriSatelliteImageRendererLog.FetchFailed(
                    logger, request.Latitude, request.Longitude,
                    $"HTTP {(int)response.StatusCode}, content-type {contentType}");
                return MapImageResult.Failure(
                    MapCaptureFailureReason.ProviderUnavailable,
                    "The imagery provider did not return an image.");
            }

            bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            EsriSatelliteImageRendererLog.FetchException(logger, exception, request.Latitude, request.Longitude);
            return MapImageResult.Failure(
                MapCaptureFailureReason.ProviderUnavailable,
                "The imagery provider could not be reached.");
        }

        IReadOnlyList<BuildingFootprint> footprints = Array.Empty<BuildingFootprint>();
        if (request.Overlays.Contains(MapCaptureOverlay.BuildingFootprints))
        {
            try
            {
                var result = await footprintProvider.SearchAsync(center, radiusMetres, cancellationToken);
                footprints = result.Buildings;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                EsriSatelliteImageRendererLog.FootprintLookupFailed(
                    logger, exception, request.Latitude, request.Longitude);
                return MapImageResult.Failure(
                    MapCaptureFailureReason.OverlayUnavailable,
                    "Building footprints could not be retrieved for this location.");
            }
        }

        return composer.Compose(bytes, center, zoom, footprints);
    }

    private string BuildUrl(double west, double south, double east, double north)
    {
        var apiKey = esriOptions.Value.ApiKey;
        var baseUrl = string.IsNullOrWhiteSpace(apiKey) ? KeylessExportUrl : AuthenticatedExportUrl;

        return baseUrl +
               $"?bbox={west.ToString("R", CultureInfo.InvariantCulture)},{south.ToString("R", CultureInfo.InvariantCulture)}," +
               $"{east.ToString("R", CultureInfo.InvariantCulture)},{north.ToString("R", CultureInfo.InvariantCulture)}" +
               "&bboxSR=4326" +
               $"&size={ImageSizePixels.ToString(CultureInfo.InvariantCulture)},{ImageSizePixels.ToString(CultureInfo.InvariantCulture)}" +
               "&imageSR=4326&format=png&f=image";
    }

    private static string WireNameOf(MapCaptureMode mode) => mode switch
    {
        MapCaptureMode.Roadmap => "roadmap",
        MapCaptureMode.Hybrid => "hybrid",
        MapCaptureMode.BuildingsOnly => "buildings-only",
        _ => "satellite",
    };
}
