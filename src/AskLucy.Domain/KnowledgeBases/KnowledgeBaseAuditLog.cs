using AskLucy.Domain.Common;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>Exactly FR-011's list — scoped narrowly; folder/document-level events are not
/// separately audited because no FR requires it (constitution §2.III YAGNI, data-model.md).</summary>
public enum KnowledgeBaseAuditAction
{
    Created,
    Edited,
    Archived,
    Restored,
    Deleted,
    PermanentlyDeleted,
    Duplicated,
}

/// <summary>
/// Immutable record of a lifecycle-relevant action on a <see cref="KnowledgeBase"/> (FR-011).
/// Append-only — no soft delete, no mutation methods, mirrors <c>ProviderHealthCheck</c>.
/// Not FK'd to <c>KnowledgeBaseId</c> with a hard foreign-key constraint that would force
/// cascade behavior — an audit entry for a permanently purged knowledge base is deliberately
/// retained (data-model.md).
/// </summary>
public sealed class KnowledgeBaseAuditLog : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public KnowledgeBaseAuditAction Action { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>A short, sanitized summary — never raw content, never a secret (constitution §14).</summary>
    public string? DetailsJson { get; private set; }

    private KnowledgeBaseAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static KnowledgeBaseAuditLog Create(Guid knowledgeBaseId, string userId, KnowledgeBaseAuditAction action, string? detailsJson, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            UserId = userId,
            Action = action,
            OccurredAtUtc = DateTime.UtcNow,
            DetailsJson = detailsJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
