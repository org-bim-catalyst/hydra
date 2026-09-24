using System.Text.Json;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// One attempt as the client is allowed to see it (specs/068 contracts/turn-outcome.md §1).
/// </summary>
/// <remarks>
/// Deliberately a projection rather than the record itself:
/// <see cref="ActionAttempt.ArgumentsJson"/> has no member here. Those are server-resolved
/// internals kept for replay, and sending them would both leak them and invite a client to send
/// them back — precisely what makes retry safe to accept as a bare message id (research.md D4).
/// </remarks>
public sealed record ActionAttemptView(string Kind, string? Key, string? TargetLabel, bool Succeeded, string? FailureReason);

/// <summary>
/// The redacted turn outcome, in the single shape the client ever receives — trailing on the
/// stream as <c>__TURN_OUTCOME__</c>, and again on an assistant message when the transcript is
/// reloaded (FR-004a, SC-001c). One type, so the two cannot drift apart.
/// </summary>
public sealed record TurnOutcomeView(
    TurnVerdict Verdict,
    IReadOnlyList<ActionAttemptView> Attempts,
    string? FailureReason,
    DateTimeOffset RecordedAtUtc)
{
    public static TurnOutcomeView From(RecordedTurnOutcome outcome) => new(
        outcome.Verdict,
        [.. outcome.Attempts.Select(a => new ActionAttemptView(a.Kind, a.Key, a.TargetLabel, a.Succeeded, a.FailureReason))],
        outcome.FailureReason,
        outcome.RecordedAtUtc);

    /// <summary>
    /// Projects a persisted <see cref="Domain.Chats.Message.TurnOutcomeJson"/> document, or
    /// <see langword="null"/> when there is none to project.
    /// </summary>
    /// <remarks>
    /// An unreadable document also yields <see langword="null"/>, which is the FR-002c
    /// "outcome unknown" state — the state that makes the claim gate <i>suppress</i> action claims
    /// rather than trust them. Failing conservatively here is the whole point: throwing would take
    /// down an entire transcript over one corrupt row, and inventing a verdict would assert
    /// something about a turn nobody knows the result of. The document itself stays in the
    /// database, unaltered, for diagnosis.
    /// </remarks>
    public static TurnOutcomeView? FromJson(string? turnOutcomeJson)
    {
        if (string.IsNullOrWhiteSpace(turnOutcomeJson))
        {
            return null;
        }

        try
        {
            var outcome = JsonSerializer.Deserialize<RecordedTurnOutcome>(turnOutcomeJson, RecordedTurnOutcomeJson.Options);
            return outcome is null ? null : From(outcome);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
