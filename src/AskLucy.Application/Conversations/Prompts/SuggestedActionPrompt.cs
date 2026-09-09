using System.Globalization;
using System.Text;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;

namespace AskLucy.Application.Conversations.Prompts;

/// <summary>
/// The offer step's system prompt (specs/045 FR-021, FR-021a, FR-021b, contracts/turn-stream.md
/// §2).
///
/// <para>
/// <b>v1.</b> A revision becomes <c>SuggestedActionPromptV2</c>, never an edit to this constant —
/// same versioning discipline as <see cref="TurnDecisionPrompt"/> (constitution §9).
/// </para>
///
/// <para>
/// <b>Kinds offered.</b> <c>capability</c> and <c>followUp</c> are composed freely, by key/text.
/// <c>flowVariant</c> (specs/045 Phase 6) is different: the model may only pick one from
/// <paramref name="flowVariantCandidates"/> below, by its compound key — it never invents a flow
/// row's arguments, because a flow has no formal schema to validate them against (see
/// <see cref="FlowVariantOfferCandidate"/>'s own remarks). <c>decline</c> is never requested from
/// the model at all (FR-022).
/// </para>
/// </summary>
public static class SuggestedActionPrompt
{
    public const string Version = "v1";

    /// <summary>
    /// Builds the system message for one offer. Pure — no I/O, no model call.
    /// </summary>
    /// <param name="offerableIndex">Only capabilities <see cref="ConversationCapabilityCatalog.OfferableFor"/> judged worth suggesting — never the full available set (FR-025b).</param>
    /// <param name="memoryContext">What Lucy remembers that might bear on this offer (FR-021b.1), or null when memory found nothing relevant.</param>
    /// <param name="justHappened">A short, factual account of what the turn just did or answered, so follow-ups and offers are composed for THIS situation rather than generically.</param>
    /// <param name="maxSubstantiveActions">The cap minus the server-appended decline row (FR-023) — stated so the model does not over-propose only to have the excess dropped and logged.</param>
    /// <param name="flowVariantCandidates">specs/045 FR-051b — already-bound flow variants worth offering, or empty when no flow is relevant this turn.</param>
    public static string Build(
        IReadOnlyList<CapabilityIndexEntry> offerableIndex,
        string? memoryContext,
        string justHappened,
        int maxSubstantiveActions,
        IReadOnlyList<FlowVariantOfferCandidate>? flowVariantCandidates = null)
    {
        flowVariantCandidates ??= [];
        var builder = new StringBuilder();

        builder.AppendLine(
            "A chat turn just finished. Decide whether Lucy should offer the user a short list of " +
            "next steps, and compose it if so. You do not perform any of these steps yourself.");
        builder.AppendLine();

        builder.AppendLine(CultureInfo.InvariantCulture, $"What just happened: {justHappened}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            builder.AppendLine("What Lucy remembers that might bear on this (research.md D14, FR-021b.1):");
            builder.AppendLine(memoryContext);
            builder.AppendLine();
        }

        builder.AppendLine("Return ONLY a single JSON object — no markdown fence, no commentary — of this exact shape:");
        builder.AppendLine(
            """{"question":"<what you ask before the list>","actions":[{"kind":"capability"|"followUp"|"flowVariant","key":"<capability key, or the flow variant's compound key>","arguments":{},"text":"<composed instruction, followUp only>","label":"<short>","description":"<one line>"}]}""");
        builder.AppendLine();

        builder.AppendLine(
            "An empty \"actions\" array is a normal, common answer (FR-025c). Return it whenever " +
            "nothing here is genuinely worth suggesting — do not pad the list, and do not repeat " +
            "back a capability just because it is available. Silence is a valid outcome.");
        builder.AppendLine();

        builder.AppendLine("Kinds of row, composed for this specific situation, not from a fixed list:");
        builder.AppendLine(
            "- \"capability\": runs one capability from the list below. Use ONLY a key from that " +
            "list, with arguments matching its hint. Anything not in the list is discarded.");
        builder.AppendLine(
            "- \"followUp\": Lucy simply says or asks something — elaborating, comparing, " +
            "clarifying an ambiguous request, or recommending something. It runs NOTHING. It " +
            "MUST NOT promise that the platform will do, fetch, search, open, move or check " +
            "anything — if the next step requires doing, propose it as a \"capability\" row " +
            "instead, never as a followUp that only sounds like one.");
        if (flowVariantCandidates.Count > 0)
        {
            builder.AppendLine(
                "- \"flowVariant\": offers a whole job someone can want as one outcome. Use ONLY " +
                "one of the compound keys listed below, EXACTLY as written, and omit \"arguments\" " +
                "entirely — it is already bound. Never invent a flowVariant key.");
        }

        builder.AppendLine();

        if (flowVariantCandidates.Count > 0)
        {
            builder.AppendLine("Flow variants worth suggesting, if genuinely relevant to what just happened:");
            foreach (var candidate in flowVariantCandidates)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {candidate.CompoundKey}: {candidate.Label} — {candidate.Description}");
            }

            builder.AppendLine();
        }

        if (offerableIndex.Count == 0)
        {
            builder.AppendLine(
                "No standalone capability is worth suggesting right now. " +
                (flowVariantCandidates.Count > 0
                    ? "Only \"followUp\" and the flow variants above are possible this turn."
                    : "Only \"followUp\" rows are possible this turn.") +
                " An empty \"actions\" array remains the right answer unless something here is genuinely useful.");
        }
        else
        {
            builder.AppendLine("Capabilities worth suggesting, if genuinely relevant to what just happened:");
            foreach (var entry in offerableIndex)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {entry.Key}: {entry.Description} {entry.WhenToUse} (needs: {entry.ArgumentHint})");
            }
        }

        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Return at most {maxSubstantiveActions} rows in \"actions\" — fewer is better, and one good suggestion beats a padded list. Do not add a decline/nothing-for-now row; the platform appends it.");

        return builder.ToString();
    }
}
