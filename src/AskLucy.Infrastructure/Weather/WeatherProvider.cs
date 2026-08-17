using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Weather;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Weather;

internal static partial class WeatherProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Weather lookup failed for ({Latitude}, {Longitude})")]
    public static partial void LookupFailed(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// Keyless implementation of <see cref="IWeatherProvider"/> (research.md Decision 6): current
/// conditions from Open-Meteo's free forecast API, reverse-geocoded to a place name via
/// OpenStreetMap Nominatim (also free, no key). Neither call carries a secret to protect — the
/// backend still mediates both for rate-limiting/observability consistency (FR-012a), not
/// credential custody. Swapping to a paid, richer provider later is an
/// <see cref="IWeatherProvider"/> implementation change only (constitution §7).
/// </summary>
public sealed class WeatherProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<WeatherOptions> options,
    ILogger<WeatherProvider> logger) : IWeatherProvider
{
    private readonly WeatherOptions _options = options.Value;

    public async Task<WeatherSnapshotDto> GetCurrentWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("Weather");

            var forecastUrl =
                $"{_options.ForecastBaseUrl}forecast?latitude={latitude}&longitude={longitude}" +
                "&current=temperature_2m,weather_code,is_day,wind_speed_10m&timezone=auto";
            using var forecastResponse = await httpClient.GetAsync(forecastUrl, cancellationToken);
            forecastResponse.EnsureSuccessStatusCode();
            var forecast = await forecastResponse.Content.ReadFromJsonAsync<OpenMeteoForecastResponse>(cancellationToken)
                ?? throw new WeatherProviderUnavailableException("The weather service returned an empty response.");

            var locationName = await ResolveLocationNameAsync(httpClient, latitude, longitude, cancellationToken);

            return new WeatherSnapshotDto(
                locationName,
                forecast.Current.Temperature2m,
                MapCondition(forecast.Current.WeatherCode, forecast.Current.WindSpeed10m),
                forecast.Current.IsDay == 1,
                DateTimeOffset.UtcNow);
        }
        catch (WeatherProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            WeatherProviderLog.LookupFailed(logger, ex, latitude, longitude);
            throw new WeatherProviderUnavailableException(
                "The weather service could not process this request. Please try again.", ex);
        }
    }

    private async Task<string> ResolveLocationNameAsync(
        HttpClient httpClient, double latitude, double longitude, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.ReverseGeocodingBaseUrl}reverse?lat={latitude}&lon={longitude}&format=json&zoom=10");
        // Nominatim's usage policy requires a descriptive User-Agent identifying the calling application.
        request.Headers.UserAgent.ParseAdd("AskLucy/1.0 (+https://hydra.bimcatalyst.com)");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return "Unknown location";
        }

        var payload = await response.Content.ReadFromJsonAsync<NominatimReverseResponse>(cancellationToken);
        var place = payload?.Address?.City ?? payload?.Address?.Town ?? payload?.Address?.Village ?? payload?.Address?.County;
        return place is not null && payload?.Address?.Country is not null
            ? $"{place}, {payload.Address.Country}"
            : payload?.DisplayName ?? "Unknown location";
    }

    private static WeatherCondition MapCondition(int weatherCode, double windSpeed10mKmh) => windSpeed10mKmh switch
    {
        >= 40 => WeatherCondition.Windy,
        _ => weatherCode switch
        {
            0 => WeatherCondition.Clear,
            1 or 2 => WeatherCondition.PartlyCloudy,
            3 => WeatherCondition.Cloudy,
            45 or 48 => WeatherCondition.Fog,
            >= 51 and <= 67 => WeatherCondition.Rain,
            >= 80 and <= 82 => WeatherCondition.Rain,
            >= 71 and <= 77 => WeatherCondition.Snow,
            85 or 86 => WeatherCondition.Snow,
            >= 95 => WeatherCondition.Thunderstorm,
            _ => WeatherCondition.Cloudy,
        },
    };

    private sealed record OpenMeteoForecastResponse([property: JsonPropertyName("current")] OpenMeteoCurrent Current);

    private sealed record OpenMeteoCurrent(
        [property: JsonPropertyName("temperature_2m")] double Temperature2m,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("is_day")] int IsDay,
        [property: JsonPropertyName("wind_speed_10m")] double WindSpeed10m);

    private sealed record NominatimReverseResponse(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed record NominatimAddress(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("country")] string? Country);
}
