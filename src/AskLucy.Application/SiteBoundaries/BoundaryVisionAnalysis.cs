using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — the reference notebook's <c>ai_boundary_analysis()</c>
/// return shape, extended with <see cref="ObservedBoundary"/> (a deliberate, user-requested
/// departure from the notebook after a live bug: a single-candidate area can have no alternative
/// to "pick," yet still be positionally wrong in its own source geometry — see
/// <see cref="BoundaryResolutionService"/>'s reconciliation for the plausibility gate that keeps
/// this from being a blank check for hallucinated coordinates). <see cref="SelectedCandidateId"/>
/// remains strictly a choice among the given candidate IDs; <see cref="ObservedBoundary"/> is a
/// separate, optional signal — the model's own visual read of where the boundary actually sits in
/// the image, geo-referenced back from image-relative coordinates, cross-checked (not blindly
/// trusted) before ever replacing mapped geometry.
/// </summary>
public sealed record BoundaryVisionAnalysis(
    bool AiUsed,
    string? SelectedCandidateId,
    double? Confidence,
    string BoundaryQuality,
    IReadOnlyList<string> Reasoning,
    IReadOnlyList<string> Issues,
    bool RequiresRefinement,
    IReadOnlyList<GeoPoint>? ObservedBoundary = null)
{
    /// <summary>
    /// The notebook's "not_configured" fallback — AI vision analysis was skipped or failed this
    /// run (disabled, no credential, no satellite image, request failure). Callers treat this
    /// exactly like the notebook does: fall back to the deterministic score alone, never as an
    /// error that should surface to the user.
    /// </summary>
    public static BoundaryVisionAnalysis NotConfigured(string reason) => new(
        AiUsed: false,
        SelectedCandidateId: null,
        Confidence: null,
        BoundaryQuality: "not_evaluated",
        Reasoning: [reason],
        Issues: [],
        RequiresRefinement: false,
        ObservedBoundary: null);
}
