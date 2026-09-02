namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution data-model.md — a <see cref="BoundaryCandidate"/> after
/// <see cref="BoundaryCandidateScorer"/> has run, carrying the combined score and its
/// per-factor breakdown (source reliability, name match, geometry quality, center proximity,
/// land-use agreement) so the winning result can explain itself (FR-005).
/// </summary>
public sealed record ScoredBoundaryCandidate(
    BoundaryCandidate Candidate,
    double Score,
    IReadOnlyDictionary<string, double> ScoreBreakdown);
