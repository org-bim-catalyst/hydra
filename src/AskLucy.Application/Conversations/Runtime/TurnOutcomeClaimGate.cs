using System.Text;
using AskLucy.Application.Ai;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// Holds back any sentence claiming an action was carried out until the turn's
/// <see cref="RecordedTurnOutcome"/> says it actually was (specs/068 FR-002, FR-002a – FR-002c,
/// contracts/turn-outcome.md §3).
///
/// <para>
/// The defect this exists for: a turn died on a dead provider credential, the next turn read the
/// resulting notice as an ordinary reply, and Lucy said "I've shown you Al Safa Park 2" with
/// nothing on screen. No prompt wording prevents that — the model has no access to what happened,
/// only to what was said. This gate does, and it is deterministic: it keeps working while the
/// provider is down, which is the exact condition that produced the defect.
/// </para>
///
/// <para>
/// <b>Bounded buffering.</b> A sentence containing no claim is released the moment its terminating
/// boundary arrives and is never held (SC-001b). Only a claim waits, and only until the outcome
/// chunk arrives — which the orchestrator yields at the end of the turn, right after the composed
/// reply. Sentences are cut with <see cref="SentenceSegmenter"/>, the same segmentation the voice
/// path uses, so voice consuming the gated stream stays in step with the text.
/// </para>
///
/// <para>
/// Stateful and single-turn. One instance per streamed turn; not thread-safe, and not meant to be —
/// a turn's deltas arrive in order on one loop.
/// </para>
/// </summary>
public sealed class TurnOutcomeClaimGate
{
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// Sentences waiting on an outcome. Once anything is held, later sentences queue behind it even
    /// when they are claim-free: releasing them first would reorder the reply.
    /// </summary>
    private readonly List<string> _held = [];

    private RecordedTurnOutcome? _outcome;
    private bool _outcomeKnown;

    /// <summary>True while the gate is holding text back — for tests and diagnostics.</summary>
    public bool IsHolding => _held.Count > 0;

    /// <summary>
    /// Takes the next content delta and returns whatever is now safe to show. May be empty, which
    /// means the sentence is still being written or is waiting on the outcome.
    /// </summary>
    public string Accept(string? contentDelta)
    {
        if (string.IsNullOrEmpty(contentDelta))
        {
            return string.Empty;
        }

        _buffer.Append(contentDelta);

        var released = new StringBuilder();
        while (SentenceSegmenter.TryTakeSegment(_buffer, out var segment))
        {
            released.Append(Admit(segment));
        }

        return released.ToString();
    }

    /// <summary>
    /// The turn's recorded outcome arrived. Returns everything that was waiting on it, verified —
    /// released untouched where the outcome bears the claim out, replaced where it does not.
    /// </summary>
    public string OutcomeRecorded(RecordedTurnOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        _outcome = outcome;
        _outcomeKnown = true;
        return DrainHeld();
    }

    /// <summary>
    /// The stream ended. Returns the trailing partial sentence plus anything still held, verified
    /// against whatever outcome is known — and with no outcome, that is FR-002c: an unverifiable
    /// claim is replaced, never released on the assumption it was true.
    /// </summary>
    public string Flush()
    {
        var released = new StringBuilder();

        if (_buffer.Length > 0)
        {
            var tail = _buffer.ToString();
            _buffer.Clear();
            released.Append(Admit(tail));
        }

        released.Append(DrainHeld());
        return released.ToString();
    }

    /// <summary>Decides, for one complete segment, whether it can go out now, wait, or be corrected.</summary>
    private string Admit(string segment)
    {
        if (_held.Count > 0)
        {
            _held.Add(segment);
            return string.Empty;
        }

        if (!ActionClaimVocabulary.IsActionClaim(segment))
        {
            return segment;
        }

        if (!_outcomeKnown)
        {
            _held.Add(segment);
            return string.Empty;
        }

        return Verify(segment);
    }

    private string DrainHeld()
    {
        if (_held.Count == 0)
        {
            return string.Empty;
        }

        var released = new StringBuilder();
        foreach (var segment in _held)
        {
            released.Append(ActionClaimVocabulary.IsActionClaim(segment) ? Verify(segment) : segment);
        }

        _held.Clear();
        return released.ToString();
    }

    /// <summary>
    /// The behaviour table in contracts/turn-outcome.md §3, asked of the recorded fact rather than
    /// of the prose.
    /// </summary>
    private string Verify(string segment)
    {
        if (!_outcomeKnown || _outcome is null)
        {
            // FR-002c — no outcome to check against. Silence about the claim, not the benefit of
            // the doubt: this branch covers a turn whose outcome could not be stored, and that is
            // precisely when a confident claim is least likely to be true.
            return Replace(segment, null);
        }

        var named = ActionClaimVocabulary.CapabilitiesNamedIn(segment);

        // A claim that names its capability is checked against that capability alone, so a turn
        // that found a place but failed to outline it can still say the first half (FR-006). A
        // claim made in general terms has nothing narrower to check than the turn as a whole.
        var supported = named.Count > 0
            ? named.Any(_outcome.SupportsSuccessClaimFor)
            : _outcome.SupportsAnySuccessClaim;

        return supported ? segment : Replace(segment, _outcome);
    }

    /// <summary>
    /// FR-002a/FR-002b — a withheld sentence is replaced by an accurate one, never dropped. Dropping
    /// it would leave a reply that reads as though it were cut off, which is its own kind of
    /// dishonesty about what happened.
    /// </summary>
    private static string Replace(string segment, RecordedTurnOutcome? outcome)
    {
        var reason = ReasonFor(segment, outcome);
        var correction = reason is null
            ? "I can't confirm that actually happened, so I won't claim it did."
            : $"I couldn't complete that: {Uncapitalize(TrimTerminator(reason))}.";

        // Carry the original's trailing whitespace across so the replacement sits in the stream the
        // way the sentence it stands in for would have.
        return correction + TrailingWhitespace(segment);
    }

    private static string? ReasonFor(string segment, RecordedTurnOutcome? outcome)
    {
        if (outcome is null)
        {
            return null;
        }

        var named = ActionClaimVocabulary.CapabilitiesNamedIn(segment);
        var matching = outcome.Attempts.FirstOrDefault(a =>
            !a.Succeeded && named.Any(k => string.Equals(a.Key ?? a.Kind, k, StringComparison.OrdinalIgnoreCase)));

        var reason = matching?.FailureReason
            ?? outcome.Attempts.FirstOrDefault(a => !a.Succeeded)?.FailureReason
            ?? outcome.FailureReason;

        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private static string TrimTerminator(string reason) => reason.TrimEnd('.', '!', '?', ' ');

    private static string Uncapitalize(string reason) =>
        reason.Length > 1 && char.IsUpper(reason[0]) && !char.IsUpper(reason[1])
            ? char.ToLowerInvariant(reason[0]) + reason[1..]
            : reason;

    private static string TrailingWhitespace(string segment)
    {
        var end = segment.Length;
        while (end > 0 && char.IsWhiteSpace(segment[end - 1]))
        {
            end--;
        }

        return segment[end..];
    }
}
