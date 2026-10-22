using AskLucy.Application.MapCaptures;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Geocoding;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.MapCaptures;

/// <summary>
/// Which provider serves a capture: the one the caller asked for, or the one an administrator
/// configured as the default.
/// <para>
/// The two renderers are not interchangeable in every respect. Esri serves satellite only, while
/// Google also serves the road, hybrid and buildings-only views; and Esri's imagery cannot be
/// assembled past zoom 18, while Google's reaches 21. A request that asks for something the
/// chosen provider cannot serve is refused with a reason, never quietly answered by the other
/// one — a picture from a different provider is not the picture that was asked for.
/// </para>
/// </summary>
public sealed class MapImageRendererRouter(
    GoogleStaticMapImageRenderer googleRenderer,
    EsriSatelliteImageRenderer esriRenderer,
    IOptions<GoogleMapsGeocodingOptions> googleOptions) : IMapImageRenderer
{
    public Task<MapImageResult> RenderAsync(
        MapImageRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Source == MapCaptureSource.Esri)
        {
            return esriRenderer.RenderAsync(request, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(googleOptions.Value.GoogleMapsApiKey))
        {
            return Task.FromResult(MapImageResult.Failure(
                MapCaptureFailureReason.ModeUnavailable,
                "No Google Maps key is configured, so Google imagery is not available."));
        }

        return googleRenderer.RenderAsync(request, cancellationToken);
    }
}
