namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// What one conversational turn actually did, persisted alongside the assistant message it
/// describes (specs/068 FR-004a, FR-004b). The single authority for every downstream consumer:
/// the claim gate, the routing summary, and retry target resolution.
///
/// <para>
/// <b>Not to be confused with</b> <see cref="Capabilities.TurnOutcome"/>, which is the specs/045
/// per-turn offer-suppression record. Different concept, adjacent namespace, and
/// <c>ConversationTurnOrchestrator</c> uses both — do not "simplify" this name back to
/// <c>TurnOutcome</c> (data-model.md §2).
/// </para>
///
/// <para>
/// Lives in the Application layer as a plain record: the layer has no EF Core reference at all, so
/// this is serialized into <c>Message.TurnOutcomeJson</c> at the persistence boundary.
/// </para>
/// </summary>
/// <param name="Verdict">What the turn did, at the coarsest grain.</param>
/// <param name="Attempts">Per-action results. May be empty; never null.</param>
/// <param name="FailureReason">Why the turn did not complete, when it did not.</param>
/// <param name="RecordedAtUtc">When this outcome was recorded.</param>
public sealed record RecordedTurnOutcome(
    TurnVerdict Verdict,
    IReadOnlyList<ActionAttempt> Attempts,
    string? FailureReason,
    DateTimeOffset RecordedAtUtc)
{
    /// <summary>The turn replied in words and attempted nothing.</summary>
    public static RecordedTurnOutcome AnsweredInWords(DateTimeOffset recordedAtUtc) =>
        new(TurnVerdict.AnsweredInWords, [], null, recordedAtUtc);

    /// <summary>
    /// The turn attempted at least one action. Whether those succeeded is read from
    /// <paramref name="attempts"/>, deliberately not from the verdict (FR-006).
    /// </summary>
    public static RecordedTurnOutcome Acted(IReadOnlyList<ActionAttempt> attempts, DateTimeOffset recordedAtUtc)
    {
        if (attempts is null || attempts.Count == 0)
        {
            throw new ArgumentException("An acting turn must record at least one attempt.", nameof(attempts));
        }

        return new RecordedTurnOutcome(TurnVerdict.Acted, attempts, null, recordedAtUtc);
    }

    /// <summary>
    /// The turn did not reach its normal end. <paramref name="attempts"/> may still be non-empty:
    /// a turn can resolve a location and then fail outlining its boundary, and the part that
    /// worked must stay recorded as having worked (FR-006).
    /// </summary>
    public static RecordedTurnOutcome FailedBeforeCompleting(
        string failureReason,
        DateTimeOffset recordedAtUtc,
        IReadOnlyList<ActionAttempt>? attempts = null)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("A failed turn must carry a reason (FR-014).", nameof(failureReason));
        }

        return new RecordedTurnOutcome(TurnVerdict.FailedBeforeCompleting, attempts ?? [], failureReason, recordedAtUtc);
    }

    /// <summary>
    /// True when this outcome supports a statement that <paramref name="capabilityKey"/> was
    /// carried out. The claim gate's question, asked of the recorded fact rather than of the prose.
    /// <para>
    /// Matches on <see cref="ActionAttempt.Key"/>, because that is the capability vocabulary the
    /// gate's patterns are generated from; <see cref="ActionAttempt.Kind"/> is the coarser
    /// suggested-action vocabulary and is only consulted for an attempt that carries no key.
    /// </para>
    /// </summary>
    public bool SupportsSuccessClaimFor(string capabilityKey) =>
        Attempts.Any(a => a.Succeeded && string.Equals(a.Key ?? a.Kind, capabilityKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when any recorded attempt succeeded, whatever its kind.</summary>
    public bool SupportsAnySuccessClaim => Attempts.Any(a => a.Succeeded);
}
