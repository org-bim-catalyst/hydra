using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>FR-047's six event types. No existing in-app notification mechanism was found anywhere in this codebase to reuse (data-model.md) — this is a feature-scoped notification, not a general-purpose platform Notification Engine.</summary>
public enum DocumentNotificationEventType
{
    UploadCompleted,
    ProcessingCompleted,
    ProcessingFailed,
    OcrFailed,
    VersionCreated,
    StorageLimitReached,
}

/// <summary>An in-app notification for a document lifecycle/processing event (FR-047, data-model.md). Delivered both by SignalR push and via a paginated inbox query (research.md Decision 7).</summary>
public sealed class DocumentNotification : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public Guid? DocumentId { get; private set; }

    public DocumentNotificationEventType EventType { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public bool IsRead { get; private set; }

    private DocumentNotification()
    {
        // Required by EF Core materialization.
    }

    public static DocumentNotification Create(string userId, Guid? documentId, DocumentNotificationEventType eventType, string message, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A notification must belong to a user.");
        }

        return new DocumentNotification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DocumentId = documentId,
            EventType = eventType,
            Message = message,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void MarkRead(string actor)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
