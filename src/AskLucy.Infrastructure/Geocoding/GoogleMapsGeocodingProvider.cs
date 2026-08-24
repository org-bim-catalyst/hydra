using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Locations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Geocoding;

internal static partial class GoogleMapsGeocodingProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Maps geocoding failed for query {Query}")]
    public static partial void SearchFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Google Maps geocoding returned non-OK status {Status} for query {Query}")]
    public static partial void NonOkStatus(ILogger logger, string status, string query);
}

/// <summary>
/// Google Maps Geocoding API implementation of <see cref="IGeocodingProvider"/>.
/// Registered by <c>DependencyInjection.AddInfrastructure</c> when
/// <c>Geocoding:GoogleMapsApiKey</c> is configured; <see cref="NominatimGeocodingProvider"/>
/// is used otherwise (local dev / environments without a key).
/// <para>
/// Importance scores are derived from the result's <c>location_type</c> so the existing
/// dominance-margin algorithm in <c>LocationResolutionService</c> works unchanged:
/// ROOFTOP=0.90, RANGE_INTERPOLATED=0.70, GEOMETRIC_CENTER=0.60, APPROXIMATE=0.40.
/// </para>
/// </summary>
internal sealed class GoogleMapsGeocodingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleMapsGeocodingOptions> options,
    ILogger<GoogleMapsGeocodingProvider> logger) : IGeocodingProvider
{
    private readonly GoogleMapsGeocodingOptions _options = options.Value;

    public async Task<IReadOnlyList<GeocodingCandidate>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("Geocoding");

            var url = $"{_options.BaseUrl}geocode/json?address={Uri.EscapeDataString(query)}&key={_options.GoogleMapsApiKey}";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var geoResponse = await response.Content.ReadFromJsonAsync<GeoResponse>(cancellationToken)
                ?? throw new JsonException("Google Maps geocoding returned null response.");

            if (geoResponse.Status == "ZERO_RESULTS")
                return [];

            if (geoResponse.Status != "OK")
            {
                GoogleMapsGeocodingProviderLog.NonOkStatus(logger, geoResponse.Status, query);
                throw new GeocodingProviderUnavailableException(
                    $"Google Maps geocoding returned status '{geoResponse.Status}' for query '{query}'.");
            }

            return geoResponse.Results
                .Select(MapCandidate)
                .ToList();
        }
        catch (GeocodingProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            GoogleMapsGeocodingProviderLog.SearchFailed(logger, ex, query);
            throw new GeocodingProviderUnavailableException(
                $"Google Maps geocoding unavailable for query '{query}'.", ex);
        }
    }

    private static GeocodingCandidate MapCandidate(GeoResult result) =>
        new(result.FormattedAddress,
            result.Geometry.Location.Lat,
            result.Geometry.Location.Lng,
            ImportanceFromLocationType(result.Geometry.LocationType),
            result.Geometry.LocationType,
            MapViewport(result.Geometry.Viewport));

    private static double ImportanceFromLocationType(string locationType) =>
        locationType switch
        {
            "ROOFTOP" => 0.90,
            "RANGE_INTERPOLATED" => 0.70,
            "GEOMETRIC_CENTER" => 0.60,
            "APPROXIMATE" => 0.40,
            _ => 0.50,
        };

    private static ViewportBounds? MapViewport(GeoViewport? viewport)
    {
        if (viewport is null) return null;
        return new ViewportBounds(
            viewport.Northeast.Lat,
            viewport.Northeast.Lng,
            viewport.Southwest.Lat,
            viewport.Southwest.Lng);
    }

    private sealed record GeoResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] GeoResult[] Results);

    private sealed record GeoResult(
        [property: JsonPropertyName("formatted_address")] string FormattedAddress,
        [property: JsonPropertyName("geometry")] GeoGeometry Geometry);

    private sealed record GeoGeometry(
        [property: JsonPropertyName("location")] GeoLocation Location,
        [property: JsonPropertyName("location_type")] string LocationType,
        [property: JsonPropertyName("viewport")] GeoViewport? Viewport);

    private sealed record GeoLocation(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lng")] double Lng);

    private sealed record GeoViewport(
        [property: JsonPropertyName("northeast")] GeoLocation Northeast,
        [property: JsonPropertyName("southwest")] GeoLocation Southwest);
}
