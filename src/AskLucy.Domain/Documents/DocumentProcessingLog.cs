using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// A timestamped record of a state transition or event (FR-013, FR-027, data-model.md) — the
/// visible processing history. Append-only: no mutation methods after creation, mirrors
/// <c>KnowledgeBaseAuditLog</c>/<c>ProviderHealthCheck</c>'s precedent of extending
/// <see cref="BaseEntity"/> without ever exercising its soft-delete/update surface.
/// </summary>
public sealed class DocumentProcessingLog : BaseEntity
{
    public Guid DocumentId { get; private set; }

    /// <summary>Null for lifecycle events not tied to a job (e.g., archive/restore/rename).</summary>
    public Guid? DocumentProcessingJobId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private DocumentProcessingLog()
    {
        // Required by EF Core materialization.
    }

    public static DocumentProcessingLog Create(Guid documentId, Guid? documentProcessingJobId, string eventType, string? detail, string actor)
    {
        return new DocumentProcessingLog
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            DocumentProcessingJobId = documentProcessingJobId,
            EventType = eventType,
            Detail = detail,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
