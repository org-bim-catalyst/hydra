using System.Globalization;
using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.MapCaptures;

internal static partial class GoogleStaticMapImageRendererLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Static Maps fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Static Maps fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Building footprint lookup failed for ({Latitude}, {Longitude}); the capture is refused rather than silently returned without the overlay")]
    public static partial void FootprintLookupFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/083-viewer-map-capture T019 — the primary renderer. Google Static Maps is what the viewer
/// renders on, so a capture matches the basemap the caller is looking at.
/// </summary>
public sealed class GoogleStaticMapImageRenderer(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> googleOptions,
    IBuildingFootprintProvider footprintProvider,
    MapImageComposer composer,
    ILogger<GoogleStaticMapImageRenderer> logger) : IMapImageRenderer
{
    private const string ClientName = "GoogleStaticMaps";
    private const string Endpoint = "staticmap";

    public async Task<MapImageResult> RenderAsync(
        MapImageRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = googleOptions.Value.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return MapImageResult.Failure(
                MapCaptureFailureReason.ModeUnavailable,
                "No Google Maps key is configured, so only satellite imagery is available.");
        }

        var radiusMetres = Math.Max(request.ViewSizeMetres / 2, 1);

        if (request.Zoom is { } requestedZoom && !MapCaptureZoom.IsInRange(requestedZoom))
        {
            // Refused rather than clamped: Google answers a level above its own with the level
            // below it, and a silently different picture is worse than an explicit refusal.
            return MapImageResult.Failure(
                MapCaptureFailureReason.ZoomUnavailable,
                $"Zoom {requestedZoom} is outside the {MapCaptureZoom.Minimum}-{MapCaptureZoom.Maximum} range Google Static Maps serves.");
        }

        var zoom = request.Zoom ?? StaticMapFraming.ChooseZoomToFit(request.Latitude, radiusMetres);
        var url = BuildUrl(request, zoom, apiKey);

        byte[] bytes;
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.GetAsync(url, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode
                || contentType is null
                || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                GoogleStaticMapImageRendererLog.FetchFailed(
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
            GoogleStaticMapImageRendererLog.FetchException(logger, exception, request.Latitude, request.Longitude);
            return MapImageResult.Failure(
                MapCaptureFailureReason.ProviderUnavailable,
                "The imagery provider could not be reached.");
        }

        IReadOnlyList<BuildingFootprint> footprints = Array.Empty<BuildingFootprint>();
        if (request.Overlays.Contains(MapCaptureOverlay.BuildingFootprints))
        {
            var lookup = await LookupFootprints(request, radiusMetres, cancellationToken);
            if (lookup is null)
            {
                return MapImageResult.Failure(
                    MapCaptureFailureReason.OverlayUnavailable,
                    "Building footprints could not be retrieved for this location.");
            }

            footprints = lookup;
        }

        return composer.Compose(
            bytes, new GeoPoint(request.Latitude, request.Longitude), zoom, footprints,
            request.CropPerEdge, request.EnhanceColour);
    }

    private static string BuildUrl(MapImageRequest request, int zoom, string apiKey)
    {
        var center = string.Create(
            CultureInfo.InvariantCulture, $"{request.Latitude},{request.Longitude}");

        var url = $"{Endpoint}?center={Uri.EscapeDataString(center)}" +
                  $"&zoom={zoom.ToString(CultureInfo.InvariantCulture)}" +
                  $"&size={StaticMapFraming.ImageSizePixels.ToString(CultureInfo.InvariantCulture)}x{StaticMapFraming.ImageSizePixels.ToString(CultureInfo.InvariantCulture)}" +
                  "&scale=2" +
                  $"&maptype={MapTypeOf(request.Mode)}" +
                  "&format=png" +
                  $"&key={Uri.EscapeDataString(apiKey)}";

        if (request.Mode == MapCaptureMode.BuildingsOnly)
        {
            // The closest Static Maps approximation of "buildings only": keep man-made land use,
            // hide everything else (research D1).
            url += "&style=feature:all|visibility:off" +
                   "&style=feature:landscape.man_made|visibility:on" +
                   "&style=feature:landscape.natural|visibility:off" +
                   "&style=feature:poi|visibility:off" +
                   "&style=feature:transit|visibility:off" +
                   "&style=feature:road|visibility:off" +
                   "&style=feature:water|visibility:off";
        }

        return url;
    }

    private static string MapTypeOf(MapCaptureMode mode) => mode switch
    {
        MapCaptureMode.Roadmap => "roadmap",
        MapCaptureMode.Hybrid => "hybrid",
        MapCaptureMode.BuildingsOnly => "roadmap",
        _ => "satellite",
    };

    private async Task<IReadOnlyList<BuildingFootprint>?> LookupFootprints(
        MapImageRequest request, int radiusMetres, CancellationToken cancellationToken)
    {
        try
        {
            var result = await footprintProvider.SearchAsync(
                new GeoPoint(request.Latitude, request.Longitude), radiusMetres, cancellationToken);
            return result.Buildings;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            GoogleStaticMapImageRendererLog.FootprintLookupFailed(
                logger, exception, request.Latitude, request.Longitude);
            return null;
        }
    }
}
