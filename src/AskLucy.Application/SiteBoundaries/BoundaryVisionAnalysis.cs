namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — a direct port of the reference notebook's
/// <c>ai_boundary_analysis()</c> return shape. The analyzer only ever chooses among (or rejects)
/// the candidate IDs it was shown — <see cref="SelectedCandidateId"/> is never a new set of
/// coordinates — so this type structurally cannot carry an invented polygon back into the
/// pipeline.
/// </summary>
public sealed record BoundaryVisionAnalysis(
    bool AiUsed,
    string? SelectedCandidateId,
    double? Confidence,
    string BoundaryQuality,
    IReadOnlyList<string> Reasoning,
    IReadOnlyList<string> Issues,
    bool RequiresRefinement)
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
        RequiresRefinement: false);
}
