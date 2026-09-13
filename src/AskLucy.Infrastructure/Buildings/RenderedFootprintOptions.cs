namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/053-rendered-building-footprints — configuration for retrieving building footprints from
/// the map provider's own rendered imagery. Bound from the "Buildings:Rendered" appsettings
/// section, mirroring <c>BuildingRetrievalOptions</c>'s shape for the Overpass side.
/// </summary>
public sealed class RenderedFootprintOptions
{
    public const string SectionName = "Buildings:Rendered";

    /// <summary>research D3 — 0.27 m/px in Dubai/Cairo at scale=2, and one tile (~345 m) covers
    /// the default 200 m analysis radius with no stitching needed.</summary>
    public int Zoom { get; set; } = 18;

    /// <summary>research D5 — below any habitable structure, above threshold noise; smaller than
    /// the boundary path's own diagonal-fraction threshold, which would discard real buildings.</summary>
    public double MinimumAreaSquareMetres { get; set; } = 15.0;

    /// <summary>Douglas-Peucker tolerance in source pixels — matches the existing vectorizer's own
    /// constant and contributes to the stated positional tolerance below (research D3).</summary>
    public double SimplifyTolerancePixels { get; set; } = 3.0;

    /// <summary>research D3 — derived from 0.27 m/px plus the simplification tolerance, not chosen.
    /// The figure quickstart Scenario 3/SC-003 is measured against.</summary>
    public double StatedPositionalToleranceMetres { get; set; } = 1.0;

    /// <summary>research D9 — this provider caches its own results internally, exactly like
    /// <c>BuildingRetrievalOptions.CacheTtl</c> on the Overpass side; there is no shared wrapper to
    /// inherit a cache from.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);
}
