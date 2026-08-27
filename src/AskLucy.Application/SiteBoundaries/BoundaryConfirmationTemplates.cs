using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — deterministic, template-based sentences for every
/// <see cref="BoundaryResolutionOutcomeType"/>, mirroring <c>LocationConfirmationTemplates</c>.
/// Source/candidate names are embedded as data into a fixed template sentence; never re-fed to
/// any LLM call as an instruction (constitution §8 prompt injection).
/// </summary>
public static class BoundaryConfirmationTemplates
{
    public static string Confirmed(string siteName, BoundaryConfidenceLevel confidenceLevel, string sourceDetail)
    {
        var confidenceText = confidenceLevel switch
        {
            BoundaryConfidenceLevel.High => "with high confidence",
            BoundaryConfidenceLevel.Medium => "with medium confidence — treat this as provisional",
            _ => "with low confidence — this is only an approximation, not a confirmed boundary",
        };

        return $"I've outlined {siteName}'s boundary {confidenceText}, based on {sourceDetail}.";
    }

    public static string WithAlternatives(string baseMessage, IReadOnlyList<string> alternativeCandidateNames) =>
        alternativeCandidateNames.Count == 0
            ? baseMessage
            : $"{baseMessage} A few other similarly-plausible boundaries were also found ({string.Join(", ", alternativeCandidateNames)}) — let me know if I picked the wrong one.";

    /// <summary>
    /// FR-005 (result explains itself) extended to the Gemini vision cross-check
    /// (<see cref="IBoundaryVisionAnalyzer"/>): appends a plain-language note only when AI
    /// analysis actually ran and either confirmed or overrode the deterministic pick, mirroring
    /// the reference notebook's own <c>agreement_msg</c>. Silent (no note) when AI verification
    /// was disabled, unavailable, or returned an unrecognized candidate id — those cases behave
    /// exactly as if AI verification didn't exist, matching prior behavior.
    /// </summary>
    public static string WithAiVerificationNote(string baseMessage, string? aiVerificationNote) =>
        string.IsNullOrEmpty(aiVerificationNote) ? baseMessage : $"{baseMessage} {aiVerificationNote}";

    public static string NoCandidates(string siteName) =>
        $"I found {siteName}'s location, but couldn't find a reliable boundary shape for it — " +
        "I'll show an approximate area instead. Let me know if you have a more specific description of its extent.";

    public const string Unavailable =
        "I couldn't look up the site boundary right now — please try again in a moment.";

    /// <summary>
    /// FR-010 — appended to the "same site still active" context message (see
    /// <c>SendChatMessageCommandHandler</c>) so a correction request is acknowledged rather than
    /// silently repeating the same result, without implying manual polygon editing exists yet.
    /// </summary>
    public const string CorrectionGuidance =
        "If the user says this boundary looks wrong, acknowledge it, ask for more specific " +
        "details that could help (e.g. a more exact address or landmark), and if none are given, " +
        "state plainly that you cannot make it more precise with the information available — " +
        "never simply repeat the same boundary as if nothing was said.";
}
