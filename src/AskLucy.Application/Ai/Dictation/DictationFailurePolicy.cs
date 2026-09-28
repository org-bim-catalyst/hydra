using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.Ai.Dictation;

/// <summary>
/// specs/078 US2/US3 — the single place a dictation engine failure is classified and reported on
/// the operational failure trail (specs/074), with the vocabulary <see cref="VoiceEngineIdentity"/>
/// the trail already uses for voice generation. Local Whisper never suspends (FR-005); a cloud
/// engine (US3) suspends only on a Critical kind (FR-016).
/// </summary>
public sealed class DictationFailurePolicy(IFailureClassifier classifier, IVoiceFailureReporter reporter)
{
    /// <summary>
    /// An engine failed before producing a transcript and the browser built-in served the user
    /// instead (FR-005/FR-007) — always a failover, never a hard failure. Returns whether the
    /// caller should suspend <paramref name="engine"/> (US3; always false when
    /// <paramref name="isCloudEngine"/> is false, as it is for every Local Whisper call site).
    /// </summary>
    public bool ReportEngineFailure(
        string operation, VoiceEngineIdentity engine, bool isCloudEngine, Exception exception, CancellationToken callerToken)
    {
        var kind = classifier.Classify(exception, callerToken);
        reporter.ReportFailover(operation, engine, exception, fallbackServed: true, callerToken);
        return isCloudEngine && kind is { } classified && OperationalFailureSeverityPolicy.IsCritical(classified);
    }

    /// <summary>A request-input problem (e.g. an invalid WAV clip) — nothing served it, so it is not a failover.</summary>
    public void ReportValidationFailure(string operation, VoiceEngineIdentity? engine, Exception exception, CancellationToken callerToken) =>
        reporter.ReportFailure(operation, engine, exception, callerToken);
}
