using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Memory;

internal static partial class MemoryCleanupJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup failed for memory {MemoryId} — will retry next cycle")]
    public static partial void MemoryCleanupFailed(ILogger logger, Guid memoryId, Exception exception);
}

/// <summary>
/// Hangfire recurring job (tasks.md T033a, research.md Decision 18; added during
/// <c>/speckit-analyze</c> remediation, finding C1 — resolves FR-031) — soft-deletes explicitly
/// expired memories and memories archived long enough ago to be considered stale, writing a
/// <see cref="MemoryAuditAction.Expired"/> audit entry for each. A simple recurring sweep with no
/// framework-free orchestration logic of its own, mirroring <c>DocumentStatisticsRecomputeJob</c>'s
/// placement in <c>Infrastructure</c>.
/// </summary>
public sealed class MemoryCleanupJob(
    IMemoryRepository memoryRepository,
    IMemoryAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<MemoryCleanupJob> logger)
{
    private const string SystemActor = "system:memory-cleanup";
    private const int BatchSize = 200;

    /// <summary>An archived memory not reinforced/touched in this long is considered stale enough to purge (spec.md FR-031 — no fixed retention period is specified, so this is a conservative default).</summary>
    private static readonly TimeSpan StaleArchivedAge = TimeSpan.FromDays(90);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var staleArchivedBeforeUtc = nowUtc - StaleArchivedAge;

        var candidates = await memoryRepository.GetCleanupCandidatesAsync(nowUtc, staleArchivedBeforeUtc, BatchSize, cancellationToken);

        foreach (var memory in candidates)
        {
            try
            {
                memory.SoftDelete(SystemActor);

                auditLogRepository.Add(MemoryAuditLog.Create(
                    memory.Id, memory.UserId, SystemActor, MemoryAuditAction.Expired,
                    JsonSerializer.Serialize(new { reason = memory.ExpiresAtUtc is not null ? "expired" : "stale-archived" }),
                    SystemActor));

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // One memory failing to clean up must not block the rest of the batch — it remains
                // a candidate and is retried next cycle.
                MemoryCleanupJobLog.MemoryCleanupFailed(logger, memory.Id, ex);
            }
        }
    }
}
