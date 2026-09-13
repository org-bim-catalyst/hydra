namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052-solar-analysis data-model.md "Building Footprint Result" — the provider's/endpoint's
/// envelope. FR-013/FR-015: an empty <see cref="Buildings"/> list is a success, not an error — "no
/// buildings here" is a real answer and the sun path must still work (FR-014).
/// </summary>
public sealed record BuildingFootprintResult(
    IReadOnlyList<BuildingFootprint> Buildings,
    bool Limited,
    int ExcludedCount,
    int RadiusMetres);
