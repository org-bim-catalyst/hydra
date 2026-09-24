using System.Text;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>One recent turn, or one action within it, as the router and the composer get to see it.</summary>
/// <param name="Kind">The capability key, where the turn named one.</param>
/// <param name="TargetLabel">What it acted on — "Al Safa Park 2".</param>
/// <param name="Verdict">"succeeded" | "failed" | "answered-only" | "did-not-complete".</param>
/// <param name="FailureReason">Why, when it did not work.</param>
public sealed record RecentTurnLine(string? Kind, string? TargetLabel, string Verdict, string? FailureReason);

/// <summary>
/// What the last few turns actually did, projected from their recorded outcomes (specs/068 FR-009,
/// data-model.md §3).
///
/// <para>
/// This is Layer 1 of the fix, and it works on the model's motive rather than on its output: a
/// composer that is told plainly "the last attempt to show Al Safa Park 2 failed" has no reason to
/// invent a success. The claim gate is Layer 2 and catches what gets through, but a turn that never
/// fabricates in the first place reads far better than one corrected after the fact.
/// </para>
///
/// <para>
/// <b>Bounded and outcome-derived.</b> At most <see cref="MaxTurns"/> lines regardless of how long
/// the conversation grows (FR-009a, SC-009), projected only from <c>TurnOutcomeJson</c> and never
/// from message prose (FR-009b) — reading the prose is how the original defect propagated. An empty
/// summary renders nothing at all, leaving the prompt byte-identical to today's (FR-009c).
/// </para>
/// </summary>
public sealed record RecentTurnOutcomeSummary(IReadOnlyList<RecentTurnLine> Turns)
{
    /// <summary>The cap from FR-009a. A constant, not a setting: the point is that it cannot grow.</summary>
    public const int MaxTurns = 3;

    public static RecentTurnOutcomeSummary Empty { get; } = new([]);

    public bool IsEmpty => Turns.Count == 0;

    /// <summary>
    /// Projects the newest <see cref="MaxTurns"/> outcomes, oldest first.
    /// </summary>
    /// <param name="outcomesOldestFirst">Recorded outcomes in conversation order; nulls are skipped.</param>
    public static RecentTurnOutcomeSummary From(IEnumerable<RecordedTurnOutcome?> outcomesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(outcomesOldestFirst);

        var recent = outcomesOldestFirst.Where(o => o is not null).TakeLast(MaxTurns);
        return new RecentTurnOutcomeSummary([.. recent.SelectMany(o => Describe(o!))]);
    }

    /// <summary>
    /// FR-006 — a turn that resolved a location and then failed to outline its boundary produces
    /// <b>two</b> lines, not one averaged verdict. Collapsing them is exactly the loss of
    /// information the requirement exists to prevent.
    /// </summary>
    private static IEnumerable<RecentTurnLine> Describe(RecordedTurnOutcome outcome)
    {
        if (outcome.Attempts.Count > 0)
        {
            foreach (var attempt in outcome.Attempts)
            {
                yield return new RecentTurnLine(
                    attempt.Key ?? attempt.Kind,
                    attempt.TargetLabel,
                    attempt.Succeeded ? "succeeded" : "failed",
                    attempt.Succeeded ? null : attempt.FailureReason);
            }

            // A turn can fail after its actions have all reported, and that is not the same fact as
            // any of them — the user still got nothing finished.
            if (outcome.Verdict == TurnVerdict.FailedBeforeCompleting)
            {
                yield return new RecentTurnLine(null, null, "did-not-complete", outcome.FailureReason);
            }

            yield break;
        }

        yield return outcome.Verdict switch
        {
            TurnVerdict.FailedBeforeCompleting => new RecentTurnLine(null, null, "did-not-complete", outcome.FailureReason),
            _ => new RecentTurnLine(null, null, "answered-only", null),
        };
    }

    /// <summary>
    /// The block handed to the model, or an empty string when there is nothing to report — callers
    /// rely on that to keep an outcome-free conversation's prompt unchanged (FR-009c).
    /// </summary>
    public string ToPromptText()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var text = new StringBuilder("What your recent turns actually did (this is the record, not a guess):\n");
        foreach (var line in Turns)
        {
            text.Append("- ");
            text.Append(line.Kind ?? "(no action)");
            if (!string.IsNullOrWhiteSpace(line.TargetLabel))
            {
                text.Append(" \"").Append(line.TargetLabel).Append('"');
            }

            text.Append(": ").Append(line.Verdict);
            if (!string.IsNullOrWhiteSpace(line.FailureReason))
            {
                text.Append(" — ").Append(line.FailureReason);
            }

            text.Append('\n');
        }

        text.Append("\nDo not say an action was carried out unless it is listed above as succeeded. " +
            "If the user asks you to try again, say plainly that the earlier attempt failed rather than " +
            "claiming it already worked.");

        return text.ToString();
    }
}
