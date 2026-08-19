using AskLucy.Application.Weather;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// research.md Decision 7 (specs/027-immersive-viewer-platform) — a small, closed set the
/// frontend maps to its own icon set, decoupled from any specific upstream provider's raw
/// condition codes (constitution §9 provider abstraction).
/// </summary>
public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Rain,
    Snow,
    Thunderstorm,
    Windy,
}

/// <summary>
/// Thrown when the upstream weather provider errors, times out, or returns an unparseable
/// response. The WebAPI layer maps this to a <c>weather-provider-unavailable</c> Problem
/// Details response (contracts/weather-api.md) — callers never see the underlying provider
/// exception.
/// </summary>
public sealed class WeatherProviderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The weather-lookup abstraction (specs/027-immersive-viewer-platform FR-009–FR-011,
/// research.md Decision 6). A single, keyless-by-default implementation
/// (<c>Infrastructure/Weather/WeatherProvider.cs</c>) today; swapping to a paid, richer
/// provider later requires only a new <c>Infrastructure</c> implementation, never a change
/// here or in the calling query handler.
/// </summary>
public interface IWeatherProvider
{
    Task<WeatherSnapshotDto> GetCurrentWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
