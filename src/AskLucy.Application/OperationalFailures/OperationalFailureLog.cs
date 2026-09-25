using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// The recording pipeline's own log (specs/074 research D2). Public because the Infrastructure
/// recorder and writer log through it too. These lines are the only place the pipeline's own
/// failures go: nothing here is ever re-recorded (FR-021).
/// </summary>
public static partial class OperationalFailureLog
{
    /// <summary>Carries the correlation id — generated here when the report had none (research D6) — so every stored record has an id that appears in the log.</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Operational failure recorded: correlationId={CorrelationId}, engine={Engine}, kind={Kind}, incident={IncidentId}, opened={Opened}")]
    public static partial void OperationalFailureRecorded(ILogger logger, string correlationId, OperationalFailureEngine engine, OperationalFailureKind kind, Guid incidentId, bool opened);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operational failure could not be recorded: correlationId={CorrelationId}, engine={Engine}, kind={Kind}")]
    public static partial void OperationalFailureRecordingFailed(ILogger logger, Exception exception, string? correlationId, OperationalFailureEngine engine, OperationalFailureKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operational failure dropped because the recording queue is full: correlationId={CorrelationId}, engine={Engine}, kind={Kind}")]
    public static partial void OperationalFailureDropped(ILogger logger, string? correlationId, OperationalFailureEngine engine, OperationalFailureKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Voice recovery could not be recorded: correlationId={CorrelationId}, kind={Kind}")]
    public static partial void VoiceRecoveryRecordingFailed(ILogger logger, Exception exception, string? correlationId, OperationalFailureKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Voice recovery ignored because no incident is open for it: correlationId={CorrelationId}, kind={Kind}")]
    public static partial void VoiceRecoveryIgnored(ILogger logger, string? correlationId, OperationalFailureKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Critical incident {IncidentId} opened, but its notification failed: correlationId={CorrelationId}")]
    public static partial void CriticalIncidentNotificationFailed(ILogger logger, Exception exception, Guid incidentId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operational failure recorder failed; the report was not queued")]
    public static partial void RecorderFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operational failure writer stopped with {AbandonedCount} report(s) still queued; they are in the server log only")]
    public static partial void ReportsAbandonedOnShutdown(ILogger logger, int abandonedCount);
}
