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
/// The satellite renderer for Esri World Imagery: analysis-grade, saturated, and without a
/// burned-in logo. It serves the same rectangle and the same frame as the Google renderer, so the
/// crop and the output contract hold whichever provider served the request.
/// </summary>
public sealed class EsriSatelliteImageRenderer(
    IHttpClientFactory httpClientFactory,
    IOptions<EsriOptions> esriOptions,
    IBuildingFootprintProvider footprintProvider,
    MapImageComposer composer,
    ILogger<EsriSatelliteImageRenderer> logger) : IMapImageRenderer
{
    private const string ClientName = "EsriWorldImagery";

    /// <summary>
    /// The World Imagery export endpoint.
    /// <para>
    /// This is the <c>services.arcgisonline.com</c> host, which serves the export directly. The
    /// ArcGIS Location Platform host (<c>ibasemaps-api.arcgis.com</c>) does <b>not</b> serve this
    /// path — it answers 404 for it — so a build pointed at that host could never capture anything.
    /// The key, when one is configured, is passed as a query token; the endpoint accepts it with or
    /// without a referer, and serves the same image either way.
    /// </para>
    /// </summary>
    private const string ExportUrl =
        "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export";

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

        if (request.Zoom is { } requestedZoom && requestedZoom > MapCaptureZoom.MaximumEsri)
        {
            // Refused rather than clamped: the export service cannot assemble its tiles past this
            // level and answers with a server error, so a clamped answer would be a different
            // picture from the one that was asked for.
            return MapImageResult.Failure(
                MapCaptureFailureReason.ZoomUnavailable,
                $"ESRI World Imagery cannot be assembled past zoom {MapCaptureZoom.MaximumEsri}; this capture asked for {requestedZoom}.");
        }

        var radiusMetres = Math.Max(request.ViewSizeMetres / 2, 1);
        var zoom = request.Zoom ?? StaticMapFraming.ChooseZoomToFit(request.Latitude, radiusMetres);
        var center = new GeoPoint(request.Latitude, request.Longitude);
        var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);

        byte[] bytes;
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get, BuildUrl(west, south, east, north));

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
                EsriSatelliteImageRendererLog.FootprintLookupFailed(logger, exception, request.Latitude, request.Longitude);
                return MapImageResult.Failure(
                    MapCaptureFailureReason.OverlayUnavailable,
                    "Building footprints could not be retrieved for this location.");
            }
        }

        // Esri's imagery is analysis-grade already — there is no web tone mapping to undo, so the
        // colour correction is never applied to it.
        return composer.Compose(
            bytes, center, zoom, footprints, request.CropPerEdge, enhanceColour: false);
    }

    private string BuildUrl(double west, double south, double east, double north)
    {
        var url = ExportUrl +
                  $"?bbox={west.ToString("R", CultureInfo.InvariantCulture)},{south.ToString("R", CultureInfo.InvariantCulture)}," +
                  $"{east.ToString("R", CultureInfo.InvariantCulture)},{north.ToString("R", CultureInfo.InvariantCulture)}" +
                  "&bboxSR=4326" +
                  $"&size={ImageSizePixels.ToString(CultureInfo.InvariantCulture)},{ImageSizePixels.ToString(CultureInfo.InvariantCulture)}" +
                  "&imageSR=4326&format=png&f=image";

        var apiKey = esriOptions.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            url += $"&token={Uri.EscapeDataString(apiKey)}";
        }

        return url;
    }

    private static string WireNameOf(MapCaptureMode mode) => mode switch
    {
        MapCaptureMode.Roadmap => "roadmap",
        MapCaptureMode.Hybrid => "hybrid",
        MapCaptureMode.BuildingsOnly => "buildings-only",
        _ => "satellite",
    };
}
