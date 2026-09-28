using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>A related item as the center shows it (contracts/notifications-api.md).</summary>
public sealed record NotificationRelatedItemDto(string Type, string Id, bool Available);

public sealed record NotificationActionDto(string? Label, string Route);

/// <summary>One notification-center item; the <c>notificationCreated</c> push carries the same shape (contracts/notification-hub.md).</summary>
public sealed record NotificationListItemDto(
    Guid Id,
    NotificationCategory Category,
    string Type,
    string Title,
    string Message,
    NotificationPriority Priority,
    NotificationStatus Status,
    string Language,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    DateTime? ExpiresAtUtc,
    NotificationActionDto? Action,
    NotificationRelatedItemDto? RelatedItem);

public enum NotificationChange
{
    Read,
    Deleted,
    Expired,
}

/// <summary>
/// Server-to-client pushes on <c>/hubs/notifications</c>. Best-effort: callers log a failure at
/// Warning and carry on, because the client refetches on reconnect (contracts/notification-hub.md).
/// </summary>
public interface INotificationRealtimePublisher
{
    Task NotificationCreatedAsync(string userId, NotificationListItemDto item, int unreadCount, CancellationToken cancellationToken);

    Task NotificationUpdatedAsync(string userId, Guid notificationId, NotificationChange change, int unreadCount, CancellationToken cancellationToken);

    Task UnreadCountChangedAsync(string userId, int unreadCount, CancellationToken cancellationToken);
}
