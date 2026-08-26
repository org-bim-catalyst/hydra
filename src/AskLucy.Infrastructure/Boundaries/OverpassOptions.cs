namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// specs/042-site-boundary-resolution — configuration for the OSM Overpass boundary-candidate
/// provider. Bound from the "Boundaries:Overpass" appsettings section, mirroring
/// <c>GeocodingOptions</c>.
/// </summary>
public sealed class OverpassOptions
{
    public const string SectionName = "Boundaries:Overpass";

    public string SearchBaseUrl { get; set; } = "https://overpass-api.de/api/";
}
