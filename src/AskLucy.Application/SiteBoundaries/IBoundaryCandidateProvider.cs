using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — searches for candidate boundary polygons around a
/// point. Mirrors <see cref="AskLucy.Application.Locations.IGeocodingProvider"/> exactly (same
/// shape, same reason: swappable, testable, provider-agnostic — constitution §3 Infrastructure
/// isolation). The only implementation in v1 is OSM Overpass; a future authoritative/cadastral
/// source is an additive <c>Infrastructure</c> implementation, not a rewrite.
/// </summary>
public interface IBoundaryCandidateProvider
{
    Task<IReadOnlyList<BoundaryCandidate>> SearchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default);
}
