using AskLucy.Domain.Documents;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Legacy;

/// <summary>
/// Maps the pre-hub <see cref="DocumentNotificationEventType"/> and <see cref="MemoryNotificationEventType"/>
/// enums onto their <see cref="NotificationTypeKeys"/> catalogue equivalents (specs/067 data-model.md
/// § Legacy mapping). Both the rewritten <c>ProcessingNotifier</c>/<c>MemoryNotifier</c> emitters
/// and US9-A's one-time importer (which replays existing <c>DocumentNotification</c>/
/// <c>MemoryNotification</c> rows onto the hub) share this single map, so the two paths can never
/// drift apart.
/// </summary>
public static class LegacyNotificationTypeMap
{
    public static string ToCatalogKey(DocumentNotificationEventType eventType) => eventType switch
    {
        DocumentNotificationEventType.UploadCompleted => NotificationTypeKeys.DocumentUploadCompleted,
        DocumentNotificationEventType.ProcessingCompleted => NotificationTypeKeys.DocumentProcessingCompleted,
        DocumentNotificationEventType.ProcessingFailed => NotificationTypeKeys.DocumentProcessingFailed,
        DocumentNotificationEventType.OcrFailed => NotificationTypeKeys.DocumentOcrFailed,
        DocumentNotificationEventType.VersionCreated => NotificationTypeKeys.DocumentVersionCreated,
        DocumentNotificationEventType.StorageLimitReached => NotificationTypeKeys.DocumentStorageLimitReached,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unmapped document notification event type."),
    };

    public static string ToCatalogKey(MemoryNotificationEventType eventType) => eventType switch
    {
        MemoryNotificationEventType.AutoCreated => NotificationTypeKeys.MemoryAutoCreated,
        MemoryNotificationEventType.AutoApproved => NotificationTypeKeys.MemoryAutoApproved,
        MemoryNotificationEventType.ConflictNeedsConfirmation => NotificationTypeKeys.MemoryConflictConfirmationNeeded,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unmapped memory notification event type."),
    };
}
