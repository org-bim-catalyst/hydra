using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Ai;

internal static partial class VoiceProviderHealthRecorderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Voice session for user {UserId} failed over to the fallback engine: {Reason}")]
    public static partial void FailedOverToFallback(ILogger logger, string userId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Voice session for user {UserId} recovered to the primary provider")]
    public static partial void RecoveredToPrimary(ILogger logger, string userId);
}

/// <summary>
/// Writes <see cref="VoiceProviderFailoverEvent"/> rows and logs failover/recovery events
/// (FR-033/FR-034/FR-039). <paramref name="reason"/> is expected to already be a short,
/// sanitized summary by the time it reaches here (constitution §14 — never the raw provider
/// exception text, which could contain the API key) — callers (research.md Decision 5) are
/// responsible for that sanitization before invoking this recorder.
/// </summary>
public sealed class VoiceProviderHealthRecorder(
    IVoiceProviderFailoverEventRepository failoverEvents,
    IUnitOfWork unitOfWork,
    ILogger<VoiceProviderHealthRecorder> logger) : IVoiceProviderHealthRecorder
{
    public async Task RecordFailoverAsync(string userId, string reason, CancellationToken cancellationToken = default)
    {
        var failoverEvent = VoiceProviderFailoverEvent.Create(
            userId, DateTime.UtcNow, VoiceProviderFailoverDirection.FailedOverToFallback, reason, userId);
        failoverEvents.Add(failoverEvent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        VoiceProviderHealthRecorderLog.FailedOverToFallback(logger, userId, reason);
    }

    public async Task RecordRecoveryAsync(string userId, CancellationToken cancellationToken = default)
    {
        var recoveryEvent = VoiceProviderFailoverEvent.Create(
            userId, DateTime.UtcNow, VoiceProviderFailoverDirection.RecoveredToPrimary, reason: null, userId);
        failoverEvents.Add(recoveryEvent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        VoiceProviderHealthRecorderLog.RecoveredToPrimary(logger, userId);
    }
}
