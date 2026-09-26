using System.Text.Json.Serialization;

namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052-solar-analysis data-model.md "Building Footprint Result" — the provider's/endpoint's
/// envelope. FR-013/FR-015: an empty <see cref="Buildings"/> list is a success, not an error — "no
/// buildings here" is a real answer and the sun path must still work (FR-014).
/// </summary>
/// <param name="Source">specs/053-rendered-building-footprints FR-015 — which source actually
/// supplied this result, appended LAST and optional (default <see cref="BuildingFootprintSource.None"/>)
/// so the two existing positional-constructor call sites this feature found
/// (<c>OverpassBuildingFootprintProvider</c>, <c>GetSiteBuildingsQueryHandlerTests</c>) are not
/// silently broken by an insertion elsewhere in the parameter list.</param>
public sealed record BuildingFootprintResult(
    IReadOnlyList<BuildingFootprint> Buildings,
    bool Limited,
    int ExcludedCount,
    int RadiusMetres,
    BuildingFootprintSource Source = BuildingFootprintSource.None);

/// <summary>FR-015 — recorded so a coverage gap degrading across a whole region is diagnosable
/// rather than invisible (research D7/D9). Explicit lower-case wire values via
/// <see cref="JsonStringEnumMemberNameAttribute"/>, mirroring <see cref="BuildingHeightProvenance"/>'s
/// own convention, independent of the API-wide enum converter's default PascalCase naming.</summary>
public enum BuildingFootprintSource
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("rendered")]
    Rendered,

    [JsonStringEnumMemberName("osm")]
    Osm,

    /// <summary>Overture Maps' buildings theme: OpenStreetMap plus Microsoft's machine-learned
    /// footprints, which fill in where OSM has no buildings mapped.</summary>
    [JsonStringEnumMemberName("overture")]
    Overture,
}
