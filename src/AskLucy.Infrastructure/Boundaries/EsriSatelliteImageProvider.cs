using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class EsriSatelliteImageProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Satellite image fetch failed for ({Latitude}, {Longitude}): {Reason}")]
    public static partial void FetchFailed(ILogger logger, double latitude, double longitude, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Satellite image fetch threw for ({Latitude}, {Longitude})")]
    public static partial void FetchException(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authenticated ESRI imagery failed for ({Latitude}, {Longitude}); retrying keyless. Check Esri:ApiKey")]
    public static partial void AuthenticatedFetchFailed(ILogger logger, double latitude, double longitude);
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
/// With <see cref="EsriOptions.ApiKey"/> set, it asks the key-authenticated endpoint first, which
/// is the licensed route for an application and meters usage against the key. The key travels
/// in a header, never the URL, so it can't end up in request logs. If that request fails (a
/// revoked or expired key, a missing privilege) it logs a warning and uses the keyless endpoint,
/// so a bad key costs the licensing, not the image.
/// </para>
/// <para>
/// Never throws (constitution §VIII) — a failed fetch returns <see langword="null"/>, and
/// <see cref="AskLucy.Application.SiteBoundaries.BoundaryResolutionService"/> treats that as
/// "AI vision verification unavailable this run," not an error.
/// </para>
/// </remarks>
internal sealed class EsriSatelliteImageProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<EsriOptions> options,
    ILogger<EsriSatelliteImageProvider> logger) : ISatelliteImageProvider
{
    private const string KeylessExportUrl = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export";
    private const string AuthenticatedExportUrl = "https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/export";
    private const int ImageSizePixels = 640;
    private const double MetersPerDegreeLatitude = 111_320.0;

    public async Task<SatelliteImage?> FetchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var image = await TryFetchAsync(AuthenticatedExportUrl, apiKey, center, radiusMeters, cancellationToken);
            if (image is not null || cancellationToken.IsCancellationRequested)
            {
                return image;
            }

            EsriSatelliteImageProviderLog.AuthenticatedFetchFailed(logger, center.Latitude, center.Longitude);
        }

        return await TryFetchAsync(KeylessExportUrl, apiKey: null, center, radiusMeters, cancellationToken);
    }

    private async Task<SatelliteImage?> TryFetchAsync(
        string exportUrl, string? apiKey, GeoPoint center, int radiusMeters, CancellationToken cancellationToken)
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
            using var request = new HttpRequestMessage(HttpMethod.Get, exportUrl + query);
            if (apiKey is not null)
            {
                request.Headers.TryAddWithoutValidation("X-Esri-Authorization", $"Bearer {apiKey}");
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);

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
