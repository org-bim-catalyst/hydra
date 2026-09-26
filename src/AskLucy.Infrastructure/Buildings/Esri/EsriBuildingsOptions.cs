namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// specs/075 — measured building heights from Esri's global 3D buildings scene layer. Bound from
/// "Buildings:Esri"; every setting has a working default, so the section can be absent.
/// </summary>
public sealed class EsriBuildingsOptions
{
    public const string SectionName = "Buildings:Esri";

    /// <summary>Turns the height source off without a deploy. Footprints then keep their assumed heights.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The I3S layer resource (not the SceneServer root). Public; no key needed.</summary>
    public string SceneLayerUrl { get; set; } =
        "https://basemaps3d.arcgis.com/arcgis/rest/services/Esri3D_Buildings_v1/SceneServer/layers/0";

    /// <summary>
    /// How long the layer description is trusted. Node pages are cached under the layer's own
    /// version string, so a republished layer is picked up within this window and never mixed
    /// with pages from the previous version.
    /// </summary>
    public TimeSpan LayerCacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Node pages near the root are shared by every request anywhere in the world.</summary>
    public TimeSpan NodePageCacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// specs/076 — the side of one roof-height cell. 2 m resolves a villa (10–20 m across) into
    /// dozens of cells while keeping a 400 m search to 160,000 cells (640 KB, cached per site).
    /// Finer buys nothing: Esri's outlines and the basemap's disagree by a few metres anyway.
    /// </summary>
    public double HeightMapCellMetres { get; set; } = 2.0;
}
