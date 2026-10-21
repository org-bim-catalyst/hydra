using AskLucy.Application.MapCaptures;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T021 — Google Static Maps when a key is configured, the ESRI
/// World Imagery fallback for satellite only when it is not.
/// </summary>
public sealed class MapImageRendererRouter(
    GoogleStaticMapImageRenderer googleRenderer,
    EsriSatelliteImageRenderer esriRenderer,
    IOptions<GoogleMapsGeocodingOptions> googleOptions) : IMapImageRenderer
{
    public Task<MapImageResult> RenderAsync(
        MapImageRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(googleOptions.Value.GoogleMapsApiKey))
        {
            return googleRenderer.RenderAsync(request, cancellationToken);
        }

        if (request.Mode == MapCaptureMode.Satellite)
        {
            return esriRenderer.RenderAsync(request, cancellationToken);
        }

        return Task.FromResult(MapImageResult.Failure(
            MapCaptureFailureReason.ModeUnavailable,
            "No Google Maps key is configured, so only satellite imagery is available."));
    }
}
