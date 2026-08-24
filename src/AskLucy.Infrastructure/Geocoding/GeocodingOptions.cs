namespace AskLucy.Infrastructure.Geocoding;

/// <summary>
/// specs/037-location-query-resolution — configuration for the Nominatim forward-geocoding
/// provider. Bound from the "Geocoding" appsettings section; independently scoped from
/// <c>WeatherOptions</c> (constitution §3 infrastructure isolation).
/// </summary>
public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    public string SearchBaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";
}
