using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation;

/// <summary>
/// specs/078 US2/US3 — the single place a dictation engine failure is classified and reported on
/// the operational failure trail (specs/074), with the vocabulary <see cref="VoiceEngineIdentity"/>
/// the trail already uses for voice generation. Local Whisper never suspends (FR-005); a cloud
/// engine (US3) suspends only on a Critical kind (FR-016).
/// </summary>
public sealed class DictationFailurePolicy(
    IFailureClassifier classifier, IVoiceFailureReporter reporter, TimeProvider timeProvider, ILogger<DictationFailurePolicy> logger)
{
    /// <summary>
    /// An engine failed before producing a transcript and the browser built-in served the user
    /// instead (FR-005/FR-007) — always a failover, never a hard failure. Returns whether the
    /// caller should suspend <paramref name="engine"/> (always false when <paramref name="isCloudEngine"/>
    /// is false, as it is for every Local Whisper call site).
    /// </summary>
    public bool ReportEngineFailure(
        string operation, VoiceEngineIdentity engine, bool isCloudEngine, Exception exception, CancellationToken callerToken)
    {
        var kind = classifier.Classify(exception, callerToken);
        reporter.ReportFailover(operation, engine, exception, fallbackServed: true, callerToken);
        return isCloudEngine && kind is { } classified && OperationalFailureSeverityPolicy.IsCritical(classified);
    }

    /// <summary>
    /// specs/078 T066 — as <see cref="ReportEngineFailure"/>, and when the failure is Critical,
    /// suspends <paramref name="suspendEngine"/> on <paramref name="setting"/> so every future turn
    /// on that vendor serves the browser built-in until an admin reverts it. The calling handler
    /// commits the mutation via its own <c>IUnitOfWork.SaveChangesAsync</c>. Never called for Local
    /// Whisper (FR-005) — <see cref="DictationEngineSetting.Suspend"/> refuses it regardless.
    /// </summary>
    public bool ReportAndMaybeSuspend(
        string operation,
        VoiceEngineIdentity engine,
        DictationEngineSetting setting,
        DictationPrimaryEngine suspendEngine,
        Exception exception,
        CancellationToken callerToken)
    {
        var kind = classifier.Classify(exception, callerToken);
        reporter.ReportFailover(operation, engine, exception, fallbackServed: true, callerToken);
        if (kind is not { } classified || !OperationalFailureSeverityPolicy.IsCritical(classified))
        {
            return false;
        }

        var reason = exception is AiProviderException ? exception.Message : classifier.FallbackReason(exception);
        var suspended = setting.Suspend(suspendEngine, FailureReasonSanitizer.Sanitize(reason), timeProvider.GetUtcNow().UtcDateTime);
        if (suspended && logger.IsEnabled(LogLevel.Warning))
        {
            DictationFailurePolicyLog.EngineSuspended(logger, suspendEngine, operation);
        }

        return suspended;
    }

    /// <summary>A request-input problem (e.g. an invalid WAV clip) — nothing served it, so it is not a failover.</summary>
    public void ReportValidationFailure(string operation, VoiceEngineIdentity? engine, Exception exception, CancellationToken callerToken) =>
        reporter.ReportFailure(operation, engine, exception, callerToken);
}

internal static partial class DictationFailurePolicyLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Dictation engine {Engine} suspended after a Critical failure during {Operation}")]
    public static partial void EngineSuspended(ILogger logger, DictationPrimaryEngine engine, string operation);
}
