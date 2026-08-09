using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>research.md Decision 14 — export runs as a background job (mirrors the platform's existing Hangfire-based async-job convention, e.g. Document Processing) rather than generating synchronously in the request.</summary>
public enum MemoryExportStatus
{
    Processing,
    Ready,
    Failed,
}

/// <summary>
/// Tracks one export request (spec.md FR-024, User Story 4 AC3, research.md Decision 14). One row
/// per request — a user requesting export twice gets two independent jobs/files, no dedup, since
/// a stale earlier export's content would otherwise silently diverge from what the status endpoint
/// implies is "the" export.
/// </summary>
public sealed class MemoryExportJob : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public MemoryExportStatus Status { get; private set; }

    /// <summary>Set only once <see cref="Status"/> is <see cref="MemoryExportStatus.Ready"/> — the <see cref="AskLucy.Application.Abstractions.IFileStorage"/> handle, never a physical path (CLAUDE.md File Management convention).</summary>
    public string? StoredFileName { get; private set; }

    public string? FailureReason { get; private set; }

    private MemoryExportJob()
    {
        // Required by EF Core materialization.
    }

    public static MemoryExportJob CreateProcessing(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("An export job must belong to a user.");
        }

        return new MemoryExportJob
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = MemoryExportStatus.Processing,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void MarkReady(string storedFileName, string actor)
    {
        Status = MemoryExportStatus.Ready;
        StoredFileName = storedFileName;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkFailed(string reason, string actor)
    {
        Status = MemoryExportStatus.Failed;
        FailureReason = reason;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public bool IsOwnedBy(string userId) => UserId == userId;
}
