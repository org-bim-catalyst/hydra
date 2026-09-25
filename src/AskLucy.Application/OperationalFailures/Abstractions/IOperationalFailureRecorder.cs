namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>
/// The seam every engine calls to put a failure on the admin trail (specs/074
/// contracts/operational-failure-recorder.md). Both methods are synchronous, non-blocking and never
/// throw: recording must never fail or slow the caller (FR-020, FR-021). Log first, then record.
/// </summary>
public interface IOperationalFailureRecorder
{
    /// <summary>Enqueues a failure for the admin trail.</summary>
    void Record(OperationalFailureReport report);

    /// <summary>Voice only: marks a recovery on the open incident with the same key; ignored when none is open.</summary>
    void RecordRecovery(VoiceRecoveryReport report);
}
