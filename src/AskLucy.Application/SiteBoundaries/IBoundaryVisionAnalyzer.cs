using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — a direct port of the reference notebook's
/// <c>ai_boundary_analysis()</c>: uses a vision-capable AI model to choose among (or reject)
/// existing deterministically-scored candidates by inspecting satellite imagery, never to invent
/// a new polygon. Never throws (constitution §VIII) — every failure path (disabled, no
/// credential, request failure, unparsable response) returns
/// <see cref="BoundaryVisionAnalysis.NotConfigured"/> so <see cref="BoundaryResolutionService"/>
/// can fall back to the deterministic score alone without special-casing exceptions.
/// </summary>
public interface IBoundaryVisionAnalyzer
{
    Task<BoundaryVisionAnalysis> AnalyzeAsync(
        SatelliteImage image,
        IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates,
        string siteName,
        GeoPoint center,
        CancellationToken cancellationToken = default);
}
