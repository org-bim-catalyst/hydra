using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class EsriSatelliteImageProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Satellite image fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Satellite image fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// Keyless fallback imagery for the vision cross-check, from ESRI World Imagery's free export
/// endpoint. Registered only when no Google Maps key is configured.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GoogleSatelliteImageProvider"/> is the primary provider and should stay that way
/// wherever a key exists. What the analyzer reads off this image is used to reposition an outline
/// that is then drawn on Google's basemap; read off a different vendor's imagery, even a perfect
/// trace lands wherever the two vendors disagree, which is a frame swap rather than a correction.
/// This provider can therefore position a boundary no better than ESRI and Google happen to
/// agree — acceptable as a degraded mode, not as the default.
/// </para>
/// <para>
/// It also takes the requested radius literally with no framing logic, so callers that want the
/// site to fill the image must size the radius themselves.
/// </para>
/// <para>
/// Never throws (constitution §VIII) — a failed fetch returns <see langword="null"/>, and
/// <see cref="AskLucy.Application.SiteBoundaries.BoundaryResolutionService"/> treats that as
/// "AI vision verification unavailable this run," not an error.
/// </para>
/// </remarks>
internal sealed class EsriSatelliteImageProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<EsriSatelliteImageProvider> logger) : ISatelliteImageProvider
{
    private const string ExportUrl = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export";
    private const int ImageSizePixels = 640;
    private const double MetersPerDegreeLatitude = 111_320.0;

    public async Task<SatelliteImage?> FetchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        try
        {
            var cosLatitude = Math.Cos(center.Latitude * Math.PI / 180.0);
            var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Max(Math.Abs(cosLatitude), 1e-6);

            var deltaLatitude = radiusMeters / MetersPerDegreeLatitude;
            var deltaLongitude = radiusMeters / metersPerDegreeLongitude;

            var west = center.Longitude - deltaLongitude;
            var south = center.Latitude - deltaLatitude;
            var east = center.Longitude + deltaLongitude;
            var north = center.Latitude + deltaLatitude;

            var query = $"?bbox={west:R},{south:R},{east:R},{north:R}&bboxSR=4326" +
                        $"&size={ImageSizePixels},{ImageSizePixels}&imageSR=4326&format=png&f=image";

            using var httpClient = httpClientFactory.CreateClient("EsriWorldImagery");
            using var response = await httpClient.GetAsync(ExportUrl + query, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                EsriSatelliteImageProviderLog.FetchFailed(logger, center.Latitude, center.Longitude, $"HTTP {(int)response.StatusCode}, content-type {contentType}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new SatelliteImage(bytes, contentType, west, south, east, north);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            EsriSatelliteImageProviderLog.FetchException(logger, ex, center.Latitude, center.Longitude);
            return null;
        }
    }
}
