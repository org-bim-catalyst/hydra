using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>spec.md FR-028's "creation, updates, approvals, rejections, and deletions" plus the two conflict events and <see cref="Expired"/> (research.md Decision 18), since those are also memory-affecting changes worth an audit trail entry.</summary>
public enum MemoryAuditAction
{
    Created,
    Approved,
    Rejected,
    Edited,
    Archived,
    Deleted,
    Expired,
    ConflictDetected,
    ConflictResolved,
}

/// <summary>
/// Security/compliance audit trail (spec.md FR-028, SC-008, Key Entity "Memory Access/Audit
/// Record"). Follows the established per-context audit-log convention (mirrors
/// <c>KnowledgeBaseAuditLog</c>/<c>DocumentAuditLog</c>) — append-only, no shared base beyond
/// <see cref="BaseEntity"/>, and deliberately <b>not</b> hard-FK'd to <see cref="Memory"/> with a
/// cascade constraint, so an audit row survives even if the memory is later hard-purged
/// (GDPR-style erasure via account deletion, research.md Decision 19).
/// </summary>
public sealed class MemoryAuditLog : BaseEntity
{
    public Guid? MemoryId { get; private set; }

    /// <summary>The memory's owner — scopes "show me my own audit trail"; never exposed to any other user (FR-028).</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Who/what performed the action — may differ from <see cref="UserId"/> for a system-actor automated action.</summary>
    public string ActorUserId { get; private set; } = string.Empty;

    public MemoryAuditAction Action { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>A short, sanitized summary — never raw content, never a secret. References the associated <see cref="MemoryVersion"/>.Id for <see cref="MemoryAuditAction.Edited"/> rather than duplicating the (already-encrypted) content a second time.</summary>
    public string? DetailsJson { get; private set; }

    private MemoryAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static MemoryAuditLog Create(Guid? memoryId, string userId, string actorUserId, MemoryAuditAction action, string? detailsJson, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            MemoryId = memoryId,
            UserId = userId,
            ActorUserId = actorUserId,
            Action = action,
            OccurredAtUtc = DateTime.UtcNow,
            DetailsJson = detailsJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
