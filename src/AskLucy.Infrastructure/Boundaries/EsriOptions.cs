namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// ArcGIS location services. Bound from the "Esri" section; every setting is optional, since
/// World Imagery also has a keyless endpoint and <see cref="EsriSatelliteImageProvider"/> falls
/// back to it.
/// </summary>
/// <remarks>
/// Keep <see cref="ApiKey"/> out of the committed appsettings.json: put it in user-secrets or the
/// untracked appsettings.Development.json / appsettings.Production.json.
/// </remarks>
public sealed class EsriOptions
{
    public const string SectionName = "Esri";

    /// <summary>An ArcGIS Location Platform API key with the Basemaps privilege.</summary>
    public string? ApiKey { get; set; }
}
