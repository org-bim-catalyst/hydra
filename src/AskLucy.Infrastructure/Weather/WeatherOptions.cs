namespace AskLucy.Infrastructure.Weather;

/// <summary>
/// research.md Decision 6 (specs/027-immersive-viewer-platform) — both upstream services are
/// keyless/free-tier by default, so unlike <c>GoogleGeminiOptions</c>/<c>PineconeOptions</c>
/// this carries no <c>ApiKey</c>. If a paid, richer provider is configured later, its key
/// would be bound the same way theirs are — environment variable/user-secrets/production
/// secret manager, never appsettings.json (constitution §8).
/// </summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    public string ForecastBaseUrl { get; init; } = "https://api.open-meteo.com/v1/";

    public string ReverseGeocodingBaseUrl { get; init; } = "https://nominatim.openstreetmap.org/";
}
