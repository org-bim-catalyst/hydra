using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Queries.GetNotifications;

/// <summary>contracts/document-processing-api.md's notification shape (FR-047).</summary>
public sealed record DocumentNotificationDto(Guid Id, Guid? DocumentId, DocumentNotificationEventType EventType, string Message, bool IsRead, DateTime CreatedAtUtc)
{
    public static DocumentNotificationDto FromEntity(DocumentNotification notification) => new(
        notification.Id, notification.DocumentId, notification.EventType, notification.Message, notification.IsRead, notification.CreatedAtUtc);
}

public sealed record DocumentNotificationPageDto(IReadOnlyList<DocumentNotificationDto> Items, string? NextCursor);
