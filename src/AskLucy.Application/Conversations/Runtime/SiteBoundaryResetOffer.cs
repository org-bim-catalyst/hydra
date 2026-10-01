using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/079 (US5, precedence row 1) - offered when Lucy reused the user's own hand-edited outline for a site
/// instead of finding one: "Reset to Lucy's outline" first, then whatever analysis rows the generic offer
/// produced for this turn, and "Keep my outline" last. Built here because its first row must be exactly
/// this capability with no arguments.
/// </summary>
public static class SiteBoundaryResetOffer
{
    public const string Question = "This is your corrected outline.";

    public static SuggestedActionOffer Build(IReadOnlyList<SuggestedAction> analysisRows)
    {
        var reset = new SuggestedAction(
            SuggestedActionKind.Capability,
            ResetSiteBoundaryCapability.CapabilityKey,
            Text: null,
            "Reset to Lucy's outline",
            "Discard your edits and show the outline I found.",
            ArgumentsJson: "{}");

        var rows = new List<SuggestedAction> { reset };
        rows.AddRange(analysisRows.Where(r => r.Kind != SuggestedActionKind.Decline));
        rows.Add(SuggestedAction.Decline("Keep my outline"));

        return new SuggestedActionOffer(Question, rows);
    }
}
