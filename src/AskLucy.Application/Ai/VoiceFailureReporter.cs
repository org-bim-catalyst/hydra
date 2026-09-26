using System.Collections.Concurrent;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.Ai;

/// <summary>The operation names voice failures are recorded and grouped under (specs/074 contracts/operational-failure-recorder.md).</summary>
public static class VoiceOperations
{
    public const string TextToSpeech = "Text-to-speech";

    public const string Transcription = "Transcription";
}

/// <summary>The voice engine a failure or recovery happened on, as the admin trail names it.</summary>
/// <param name="ProviderId">The <c>VoiceProvider</c> row, which the corrective action selects on the voice page.</param>
public sealed record VoiceEngineIdentity(string ProviderName, Guid? ProviderId = null, string? Model = null);

/// <summary>
/// specs/074 US2 — puts voice failovers and recoveries on the operational failure trail. A failover
/// is remembered per user, operation and engine so that the next time the same engine serves that
/// user, the recovery lands on the incident the failover joined (same grouping key). Never throws,
/// and marks every exception it records so the Problem Details boundary does not record it again.
/// </summary>
public interface IVoiceFailureReporter
{
    /// <summary>An engine failed before producing anything; <paramref name="fallbackServed"/> says whether another source served the user.</summary>
    void ReportFailover(string operation, VoiceEngineIdentity engine, Exception exception, bool fallbackServed, CancellationToken callerToken);

    /// <summary>A failure that is not a failover: audio cut off mid-sentence, or no engine to try at all.</summary>
    void ReportFailure(string operation, VoiceEngineIdentity? engine, Exception exception, CancellationToken callerToken);

    /// <summary>The engine served the current user; records a recovery if it last failed over for them.</summary>
    void ReportServed(string operation, VoiceEngineIdentity engine);
}

/// <summary>
/// The failovers still waiting for a recovery, shared across requests. Held in memory only: after a
/// restart a recovery finds nothing to match and is not recorded, the same outcome as a recovery
/// arriving after the incident was resolved (spec Edge Cases). Bounded by users × operations ×
/// engines, and each recovery removes its entry.
/// </summary>
public sealed class VoiceFailoverMemory
{
    private readonly ConcurrentDictionary<(string UserId, string Operation, string ProviderName), VoiceRecoveryReport> _pending = new();

    public void Remember(string userId, VoiceRecoveryReport recovery) =>
        _pending[(userId, recovery.Operation, recovery.ProviderName ?? string.Empty)] = recovery;

    public bool TryTake(string userId, string operation, string providerName, out VoiceRecoveryReport? recovery) =>
        _pending.TryRemove((userId, operation, providerName), out recovery);
}

/// <inheritdoc />
public sealed class VoiceFailureReporter(
    IOperationalFailureRecorder recorder,
    IFailureClassifier classifier,
    VoiceFailoverMemory memory,
    ICurrentUserAccessor currentUser) : IVoiceFailureReporter
{
    public void ReportFailover(string operation, VoiceEngineIdentity engine, Exception exception, bool fallbackServed, CancellationToken callerToken)
    {
        var outcome = fallbackServed ? OperationalFailureOutcome.DegradedServed : OperationalFailureOutcome.Failed;
        var kind = Record(operation, engine, exception, outcome, isFailover: true, callerToken);
        if (kind is null || currentUser.UserId is not { } userId)
        {
            return;
        }

        // The recovery must rebuild the failover's grouping key, so it replays the failover's own
        // kind and model rather than whatever the successful attempt used.
        memory.Remember(userId, new VoiceRecoveryReport
        {
            Operation = operation,
            Kind = kind.Value,
            ProviderName = engine.ProviderName,
            Model = engine.Model,
            UserId = userId,
        });
    }

    public void ReportFailure(string operation, VoiceEngineIdentity? engine, Exception exception, CancellationToken callerToken) =>
        Record(operation, engine, exception, OperationalFailureOutcome.Failed, isFailover: false, callerToken);

    public void ReportServed(string operation, VoiceEngineIdentity engine)
    {
        if (currentUser.UserId is { } userId && memory.TryTake(userId, operation, engine.ProviderName, out var recovery) && recovery is not null)
        {
            recorder.RecordRecovery(recovery);
        }
    }

    /// <returns>The recorded kind, or null when the caller's own cancellation means nothing is recorded (FR-006a).</returns>
    private OperationalFailureKind? Record(
        string operation,
        VoiceEngineIdentity? engine,
        Exception exception,
        OperationalFailureOutcome outcome,
        bool isFailover,
        CancellationToken callerToken)
    {
        var kind = classifier.Classify(exception, callerToken);
        if (kind is null)
        {
            return null;
        }

        recorder.Record(new OperationalFailureReport
        {
            Engine = OperationalFailureEngine.Voice,
            Operation = operation,
            Kind = kind.Value,
            Outcome = outcome,

            // A provider exception's message is our own classified prose; anything else is named by
            // type only. Either way the ingestor sanitises it again before it is stored.
            Reason = exception is AiProviderException ? exception.Message : classifier.FallbackReason(exception),
            Exception = exception,
            ProviderId = engine?.ProviderId,
            ProviderName = engine?.ProviderName,
            Model = engine?.Model,
            References = new OperationalFailureReferences { UserId = currentUser.UserId },
            IsFailover = isFailover,
        });
        exception.MarkOperationalFailureRecorded();
        return kind;
    }
}
