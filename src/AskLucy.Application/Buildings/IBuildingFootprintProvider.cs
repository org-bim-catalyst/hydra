using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052-solar-analysis research D4 — searches for building footprints around a point.
/// Mirrors <see cref="AskLucy.Application.SiteBoundaries.IBoundaryCandidateProvider"/> exactly
/// (same shape, same reason: swappable, testable, provider-agnostic — constitution §3
/// Infrastructure isolation). The only implementation in v1 is OSM Overpass; a future
/// authoritative/cadastral source is an additive <c>Infrastructure</c> implementation, not a
/// rewrite.
/// </summary>
public interface IBuildingFootprintProvider
{
    Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default);
}
