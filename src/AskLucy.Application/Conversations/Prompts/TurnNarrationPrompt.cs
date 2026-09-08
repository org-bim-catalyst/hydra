using System.Globalization;
using System.Text;

namespace AskLucy.Application.Conversations.Prompts;

/// <summary>
/// The prompt that turns a capability's real result into the sentence a user reads
/// (specs/045 FR-007, FR-036).
///
/// <para>
/// <b>v1.</b> A revision becomes <c>TurnNarrationPromptV2</c>, never an edit here.
/// </para>
///
/// <para>
/// This is the half of the turn that had to stop being canned text. Until specs/045 the
/// application appended a fixed sentence — "I've located X and centred the viewer on it" — to a
/// reply the model had already finished writing before it knew what happened. That sentence was
/// always the same whether the lookup was confident or marginal, and the reply beside it could
/// not refer to the result at all. Narration is generated from the actual outcome so it can say
/// what really occurred, including when that is a failure.
/// </para>
/// </summary>
public static class TurnNarrationPrompt
{
    public const string Version = "v1";

    /// <summary>
    /// Builds the system message for reporting one completed capability.
    /// </summary>
    /// <param name="capabilityLabel">User-facing name of what ran.</param>
    /// <param name="usageGuidance">The capability's Tier 2 guidance — how to report its result well.</param>
    /// <param name="succeeded">Whether it produced a usable result.</param>
    /// <param name="resultJson">The capability's own output, or its failure reason.</param>
    /// <param name="nextStepLabel">The step starting next, when one is, so the report and the next announcement travel in one message (FR-053).</param>
    public static string Build(
        string capabilityLabel,
        string usageGuidance,
        bool succeeded,
        string resultJson,
        string? nextStepLabel)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are reporting, to the user, what just happened. Write the message they will read.");
        builder.AppendLine();

        builder.AppendLine(CultureInfo.InvariantCulture, $"What ran: {capabilityLabel}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Outcome: {(succeeded ? "succeeded" : "did not succeed")}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Result: {resultJson}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(usageGuidance))
        {
            builder.AppendLine("How to report this particular kind of result:");
            builder.AppendLine(usageGuidance);
            builder.AppendLine();
        }

        builder.AppendLine("Rules:");
        builder.AppendLine("- One or two sentences. This is a status report, not an essay.");

        // The single most important constraint. A narration that reads as success when the
        // capability failed is worse than no narration at all, because the user acts on it.
        builder.AppendLine(
            "- Say what ACTUALLY happened. If the outcome did not succeed, say so plainly and " +
            "name the reason — never describe a success that did not occur, and never soften a " +
            "failure into something that sounds like one.");
        builder.AppendLine("- Use only the result above. Do not add detail you were not given.");
        builder.AppendLine(
            "- Do not describe the place, its facilities, history or how to get there. The user " +
            "asked for an action, not an article.");
        builder.AppendLine("- No preamble, no restating the request, no markdown headings.");

        if (!string.IsNullOrWhiteSpace(nextStepLabel))
        {
            // FR-053 — the completion and the next announcement travel together, which is what
            // makes announcing every step affordable rather than doubling the message count.
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- Then, in the same message, say you are moving on to: {nextStepLabel}. One short clause.");
        }

        return builder.ToString();
    }
}
