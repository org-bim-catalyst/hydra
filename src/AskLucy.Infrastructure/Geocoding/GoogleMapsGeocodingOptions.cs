namespace AskLucy.Infrastructure.Geocoding;

/// <summary>
/// Configuration for the Google Maps Geocoding API provider. Bound from the shared
/// "Geocoding" appsettings section alongside <see cref="GeocodingOptions"/>.
/// <para>
/// The API key must be a server-side key (IP-restricted or unrestricted) — not the
/// domain-restricted browser key used by the frontend viewer, which is rejected by
/// server-to-server calls.
/// </para>
/// </summary>
public sealed class GoogleMapsGeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>When empty, <see cref="IGeocodingProvider"/> falls back to Nominatim.</summary>
    public string GoogleMapsApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/";
}
