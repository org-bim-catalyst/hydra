using System.Text.Json.Serialization;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052-solar-analysis data-model.md "Building" / contracts/building-footprints-endpoint.md —
/// one building footprint near the analysed site, resolved server-side so the browser never
/// re-parses an OSM tag (research D5). Rendering note (research D13): the client extrudes this
/// into shadow-casting geometry that is not drawn — nothing in this shape changes as a result.
/// </summary>
public sealed record BuildingFootprint(
    string Id,
    IReadOnlyList<GeoPoint> Ring,
    double HeightMetres,
    BuildingHeightProvenance HeightProvenance,
    string Name,
    bool IsSiteBuilding);

/// <summary>FR-011 — whether a building's height was recorded in the source data or assumed via
/// the stated fallback rule (research D5). Explicit lower-case wire values (contracts/building-
/// footprints-endpoint.md: "known"/"assumed") via <see cref="JsonStringEnumMemberNameAttribute"/>,
/// independent of the API-wide enum converter's default PascalCase naming (Program.cs) — that
/// global converter must not be reconfigured just for this one DTO, since every other enum's
/// existing wire casing depends on it.</summary>
public enum BuildingHeightProvenance
{
    [JsonStringEnumMemberName("known")]
    Known,

    [JsonStringEnumMemberName("assumed")]
    Assumed,
}
