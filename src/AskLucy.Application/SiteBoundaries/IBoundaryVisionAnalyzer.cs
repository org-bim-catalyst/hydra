using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — originally a direct port of the reference notebook's
/// <c>ai_boundary_analysis()</c>, which chose among existing deterministically-scored candidates
/// rather than ever inventing a polygon. <see cref="BoundaryVisionAnalysis.ObservedBoundary"/> has
/// since taken over as the primary signal (§9.7): the shipped implementation traces the boundary
/// directly from a rendered map image and <see cref="BoundaryResolutionService"/> adopts that trace
/// outright once it clears a plausibility check, rather than only selecting among candidates.
/// <c>rankedCandidates</c> remains part of the contract for an implementation's own use (an
/// early-exit guard, framing the image on the right site) — an implementation is free to also
/// surface them to the model for <see cref="BoundaryVisionAnalysis.SelectedCandidateId"/>, which
/// <see cref="BoundaryResolutionService"/> still honours as an override signal, but the shipped
/// Gemini implementation deliberately does not: it shows the model no OSM data at all. Never throws
/// (constitution §VIII) — every failure path (disabled, no credential, request failure, unparsable
/// response) returns <see cref="BoundaryVisionAnalysis.NotConfigured"/> so
/// <see cref="BoundaryResolutionService"/> can fall back to the deterministic score alone without
/// special-casing exceptions.
/// </summary>
public interface IBoundaryVisionAnalyzer
{
    Task<BoundaryVisionAnalysis> AnalyzeAsync(
        SatelliteImage image,
        IReadOnlyList<StreetViewImage> streetViews,
        IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates,
        string siteName,
        GeoPoint center,
        CancellationToken cancellationToken = default);
}
