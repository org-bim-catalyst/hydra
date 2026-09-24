namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// What one conversational turn did, at the coarsest grain (specs/068 FR-005).
/// <para>
/// Serialized as a string, per the API-wide enum converter — a numeric value on the wire would
/// break every frontend string comparison.
/// </para>
/// </summary>
public enum TurnVerdict
{
    /// <summary>The turn replied without attempting any workspace action.</summary>
    AnsweredInWords = 0,

    /// <summary>
    /// At least one action was attempted. Deliberately carries <b>no</b> aggregate pass/fail:
    /// FR-006 requires partial success be reported per part, and a single verdict covering every
    /// attempt would destroy exactly the information that requirement exists to preserve. Read
    /// <see cref="RecordedTurnOutcome.Attempts"/> for what actually happened.
    /// </summary>
    Acted = 1,

    /// <summary>
    /// The turn did not reach its normal end — the mid-stream catch path. This is the state that
    /// went unrecorded in production: the failure notice was persisted as ordinary assistant
    /// prose, indistinguishable from a real reply, and a later turn read it as one.
    /// </summary>
    FailedBeforeCompleting = 2,
}
