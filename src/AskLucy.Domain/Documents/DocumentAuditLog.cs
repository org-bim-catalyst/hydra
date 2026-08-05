using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// An immutable record of a security- or lifecycle-relevant action (FR-051, data-model.md) —
/// deliberately distinct from <see cref="DocumentProcessingLog"/> (the processing/lifecycle
/// trail); this is the security/audit trail. Append-only, mirrors
/// <c>KnowledgeBaseAuditLog</c>/<c>ProviderHealthCheck</c>.
/// </summary>
public sealed class DocumentAuditLog : BaseEntity
{
    /// <summary>Null for events not tied to a specific document (e.g., a rejected upload that never became a <see cref="Document"/> row).</summary>
    public Guid? DocumentId { get; private set; }

    public string ActorUserId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private DocumentAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static DocumentAuditLog Create(Guid? documentId, string actorUserId, string eventType, string? detail, string actor)
    {
        return new DocumentAuditLog
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            ActorUserId = actorUserId,
            EventType = eventType,
            Detail = detail,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
