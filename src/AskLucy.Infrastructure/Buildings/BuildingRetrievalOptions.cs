namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/052-solar-analysis research D4 — configuration for building-footprint retrieval. Bound
/// from the "Buildings:Overpass" appsettings section, mirroring <c>OverpassOptions</c>.
/// </summary>
public sealed class BuildingRetrievalOptions
{
    public const string SectionName = "Buildings:Overpass";

    /// <summary>contracts/building-footprints-endpoint.md — the reference implementation's proven
    /// working value.</summary>
    public int DefaultRadiusMetres { get; set; } = 200;

    /// <summary>FR-015 — the hard cap on returned buildings; exceeding it sets
    /// <see cref="BuildingFootprintResult.Limited"/> and the count actually available.</summary>
    public int MaxBuildingCount { get; set; } = 300;

    /// <summary>research D4 — required, not an optimisation: the spec's own Clarification
    /// justifies routing building data through the platform partly on caching. Building footprints
    /// are near-static while Overpass is the least reliable dependency in the system.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);
}
