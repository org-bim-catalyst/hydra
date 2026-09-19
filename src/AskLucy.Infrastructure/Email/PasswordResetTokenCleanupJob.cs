using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Hangfire recurring job (specs/058-password-recovery T054) — drops reset tokens that are both
/// spent and old. A consumed, superseded or expired token is already inert, so this is retention
/// rather than security: without it the table keeps a permanent per-user history of every recovery
/// attempt, which is data we have no reason to hold.
/// <para>
/// Batched, and deliberately not looped: one pass per run keeps each execution bounded, and a
/// backlog drains over successive days rather than in one long-running transaction.
/// </para>
/// </summary>
public sealed partial class PasswordResetTokenCleanupJob(
    IPasswordResetTokenRepository resetTokenRepository,
    ILogger<PasswordResetTokenCleanupJob> logger)
{
    private const int BatchSize = 500;

    /// <summary>Long enough that a token remains available for incident review, short enough not to be a standing record.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await resetTokenRepository.DeleteSpentBeforeAsync(
            DateTime.UtcNow - Retention, BatchSize, cancellationToken);

        if (deleted > 0)
        {
            LogTokensDeleted(logger, deleted, Retention.TotalDays);
        }
    }

    [LoggerMessage(EventId = 5840, Level = LogLevel.Information, Message = "Deleted {DeletedCount} spent password reset tokens older than {RetentionDays} days.")]
    private static partial void LogTokensDeleted(ILogger logger, int deletedCount, double retentionDays);
}
