namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution FR-004 — the user-facing confidence classification for a
/// resolved <see cref="SiteBoundaryResult"/>. Always derived from a numeric score against
/// configured thresholds (BoundaryScoringOptions) — never set directly by a caller.
/// </summary>
public enum BoundaryConfidenceLevel
{
    Low,
    Medium,
    High,
}
