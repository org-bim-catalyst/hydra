using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Locations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Geocoding;

internal static partial class NominatimGeocodingProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Nominatim forward-geocoding failed for query {Query}")]
    public static partial void SearchFailed(ILogger logger, Exception exception, string query);
}

/// <summary>
/// specs/037-location-query-resolution — Nominatim-backed implementation of
/// <see cref="IGeocodingProvider"/>. Reuses the existing "Weather" named
/// <see cref="HttpClient"/> (same Nominatim host, same 15 s timeout) so no new
/// infrastructure is required. Cache-free by design so spec 035 FR-017 can wrap
/// <see cref="IGeocodingProvider"/> with a caching decorator without modifying this class.
/// </summary>
internal sealed class NominatimGeocodingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GeocodingOptions> options,
    ILogger<NominatimGeocodingProvider> logger) : IGeocodingProvider
{
    private const string UserAgentHeader = "AskLucy/1.0 (+https://hydra.bimcatalyst.com)";

    private readonly GeocodingOptions _options = options.Value;

    public async Task<IReadOnlyList<GeocodingCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("Geocoding");

            var url = $"{_options.SearchBaseUrl}search?q={Uri.EscapeDataString(query)}&format=json&addressdetails=1&limit=5";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var results = await response.Content.ReadFromJsonAsync<NominatimResult[]>(cancellationToken)
                ?? [];

            return results
                .Select(r => TryMapCandidate(r))
                .OfType<GeocodingCandidate>()
                .ToList();
        }
        catch (GeocodingProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            NominatimGeocodingProviderLog.SearchFailed(logger, ex, query);
            throw new GeocodingProviderUnavailableException($"Nominatim geocoding unavailable for query '{query}'.", ex);
        }
    }

    private static GeocodingCandidate? TryMapCandidate(NominatimResult result)
    {
        if (!double.TryParse(result.Lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
            return null;
        if (!double.TryParse(result.Lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
            return null;

        return new GeocodingCandidate(result.DisplayName, lat, lon, result.Importance);
    }

    private sealed record NominatimResult(
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("importance")] double Importance);
}
