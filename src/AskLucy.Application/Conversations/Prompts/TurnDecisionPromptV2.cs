using System.Globalization;
using System.Text;
using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Prompts;

/// <summary>
/// The decide step's system prompt (specs/045 FR-036, constitution §9 "prompts are versioned
/// artifacts, reviewed like code, and testable in isolation from the model call").
///
/// <para>
/// <b>v2.</b> A full copy of <see cref="TurnDecisionPrompt"/> rather than an edit to it, which is
/// what that class's own remarks require: the wording is what a routing benchmark is measured
/// against, so each version has to stay readable at the revision it was measured at. v1 is kept,
/// unreferenced by the runtime, for exactly that comparison.
/// </para>
///
/// <para>
/// <b>What changed from v1, and why.</b> v1 defined "suggest" only by example — "do you know X?",
/// "what is X?" — and every one of those examples names a <i>subject</i>. A message that names no
/// subject and instead asks what is possible at all ("what can you do?", "give me options", "what
/// else can you show me?") matched none of them, so it routed to "answer" — and "answer" is the one
/// intent that suppresses the offer step outright (<see cref="Runtime.OfferSuppressionRules"/>,
/// FR-025a.1). A user who asked for options structurally could not be given any: they got prose
/// describing choices that were never rendered. v2 names that class of message explicitly.
/// </para>
///
/// <para>
/// Carries the <b>Tier 1 index only</b>: key, what it does, when to use it, argument hint. Input
/// schemas are Tier 3 and are deliberately withheld (research.md D13), so the model cannot shape
/// its arguments around the validation they back — which is what makes the grounder's check
/// independent rather than a formality.
/// </para>
/// </summary>
public static class TurnDecisionPromptV2
{
    public const string Version = "v2";

    /// <summary>
    /// Builds the system message for one turn. Pure — no I/O, no model call — so the prompt can
    /// be asserted on directly in tests, which is the point of §9's "testable in isolation".
    /// </summary>
    /// <param name="index">Available capabilities (Tier 1 — key/description/whenToUse/argumentHint).</param>
    /// <param name="flowIndex">
    /// specs/045 Phase 6 — available flows, shown the same way (a flow's index entry is the same
    /// shape as a capability's, research.md D17): the model chooses a job, not a pipeline.
    /// </param>
    /// <param name="recentOutcomes">
    /// specs/068 FR-009 — what the last few turns actually did, so "try again" routes to another
    /// attempt instead of being read as a fresh request. Omitted entirely when empty.
    /// </param>
    public static string Build(
        IReadOnlyList<CapabilityIndexEntry> index,
        IReadOnlyList<CapabilityIndexEntry>? flowIndex = null,
        Runtime.RecentTurnOutcomeSummary? recentOutcomes = null)
    {
        flowIndex ??= [];
        var builder = new StringBuilder();

        builder.AppendLine(
            "You decide what a single chat turn needs. You do not answer the user and you do not " +
            "perform work; you choose what should happen, and something else carries it out.");
        builder.AppendLine();

        builder.AppendLine("Return ONLY a single JSON object — no markdown fence, no commentary — of this exact shape:");
        builder.AppendLine(
            """{"intent":"answer"|"act"|"suggest","slices":[{"capabilityKey":"<key>","arguments":{},"pendingLabel":"<short label>","dependsOn":null}],"flowKey":null,"flowArguments":{},"throughStepIndex":null}""");
        builder.AppendLine(
            "flowKey/flowArguments/throughStepIndex are used INSTEAD of slices when a whole flow " +
            "fits (below) — omit or leave slices [] in that case. Omit flowKey entirely when no " +
            "flow applies.");
        builder.AppendLine();

        builder.AppendLine("Choose the intent by what the user is actually asking for:");
        builder.AppendLine(
            "- \"answer\": the message can be answered in words and needs nothing offered " +
            "afterwards. A greeting, a follow-up about something already said, a factual question " +
            "about the world, or a place named only in passing (\"I read that X was renovated\"). " +
            "slices must be [] and flowKey must be omitted.");
        builder.AppendLine(
            "- \"act\": the user is asking for something to be done — \"show me X\", \"take me " +
            "to X\", \"outline the site\", \"search my documents for X\". Populate slices, or " +
            "name a flow when one fits the whole request better than a single capability.");
        builder.AppendLine(
            "- \"suggest\": the user asked ABOUT something rather than asking for it to be done " +
            "(\"do you know X?\", \"what is X?\"), OR asked what is possible rather than about any " +
            "subject at all (\"what can you do?\", \"give me options\", \"what else can you show " +
            "me?\", \"any suggestions?\", \"what now?\"). Answer in words, and the platform will " +
            "offer the related actions rather than performing them. slices must be []. If a flow " +
            "is relevant to what was asked about, still name it as flowKey (with flowArguments) so " +
            "its variants can be offered — this does not run it.");
        builder.AppendLine();

        // The failure this line exists to stop: "answer" is the only intent that skips the offer
        // step entirely, so routing a request for options there is the one certain way to make the
        // options unreachable. Naming the consequence is what makes a model treat it as a rule.
        builder.AppendLine(
            "A message asking what is possible is NEVER \"answer\". Only \"suggest\" lets the " +
            "platform attach the choices; routed to \"answer\" it produces a description of " +
            "options the user is then given no way to pick from.");
        builder.AppendLine();

        // The asymmetry is the whole reason this instruction exists, and stating the cost is what
        // makes the model apply it rather than treat it as a stylistic preference.
        builder.AppendLine(
            "When a message sits between \"act\" and \"suggest\", choose \"suggest\". Offering when " +
            "you should have acted costs the user one click; acting when you should have offered " +
            "moves their map uninvited and can spend thirty seconds doing it.");
        builder.AppendLine();

        if (flowIndex.Count > 0)
        {
            builder.AppendLine(
                "Available flows — a flow is ONE job made of several dependent steps. Prefer a " +
                "flow over a single capability whenever the whole job is what was actually asked " +
                "for (\"show me X\" wants the place found AND shown, not just found):");
            foreach (var flow in flowIndex)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {flow.Key}: {flow.Description} {flow.WhenToUse} (needs: {flow.ArgumentHint})");
            }

            builder.AppendLine(
                "- throughStepIndex (0-based) scopes a flow run short of every step, only when the " +
                "user explicitly limited it (\"just find it, don't outline it\" → 0). Omit it to " +
                "run the whole flow — that is the normal case for a navigational request.");
            builder.AppendLine();
        }

        if (index.Count == 0 && flowIndex.Count == 0)
        {
            builder.AppendLine("Nothing is available this turn, so intent must be \"answer\" with slices [] and flowKey omitted.");
            return builder.ToString();
        }

        if (index.Count > 0)
        {
            builder.AppendLine("Available capabilities. Use ONLY these keys — any other key is discarded:");
            foreach (var entry in index)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {entry.Key}: {entry.Description} {entry.WhenToUse} (needs: {entry.ArgumentHint})");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Rules for slices:");
        builder.AppendLine("- Only include a slice when the capability is genuinely needed. Fewer is better.");
        builder.AppendLine("- pendingLabel is what the user reads while it runs — short, present tense, naming the work.");
        builder.AppendLine("- dependsOn is the 0-based index of an earlier slice whose result this one needs, or null.");
        builder.AppendLine("- Never invent a capability or a flow. If nothing listed fits, use intent \"answer\".");

        var outcomes = recentOutcomes?.ToRouterText() ?? string.Empty;
        if (outcomes.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine(outcomes);
            builder.AppendLine(
                "A message like \"try again\" or \"do it again\" refers to the most recent failed " +
                "turn above: decide \"act\" and name the same capability with the same arguments. " +
                "Never treat a retry as a question about what already happened.");
        }

        return builder.ToString();
    }
}
