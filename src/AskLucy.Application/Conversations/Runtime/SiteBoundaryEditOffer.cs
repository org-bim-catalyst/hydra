using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Conversations;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/079 (research D6) - offered once an outline is final: "want to adjust its corners?", with
/// the question worded by how sure the outline is, so the user knows whether a look is worth their
/// while. "Edit the outline" comes first, then whatever analysis rows the generic offer produced for
/// this turn, and "It looks right" last. Built here rather than composed by the offer model because
/// its first row must be exactly this capability with no arguments.
/// </summary>
public static class SiteBoundaryEditOffer
{
    public static SuggestedActionOffer Build(BoundaryConfidenceLevel level, IReadOnlyList<SuggestedAction> analysisRows)
    {
        var certainty = level switch
        {
            BoundaryConfidenceLevel.High => "I'm confident about",
            BoundaryConfidenceLevel.Medium => "I'm fairly sure about",
            _ => "I'm not sure about",
        };

        var edit = new SuggestedAction(
            SuggestedActionKind.Capability,
            EditSiteBoundaryCapability.CapabilityKey,
            Text: null,
            "Edit the outline",
            "Move, add or delete the outline's corners on the map.",
            ArgumentsJson: "{}");

        var rows = new List<SuggestedAction> { edit };
        rows.AddRange(analysisRows.Where(r => r.Kind != SuggestedActionKind.Decline));
        rows.Add(SuggestedAction.Decline("It looks right"));

        return new SuggestedActionOffer($"{certainty} this outline — want to adjust its corners?", rows);
    }
}
