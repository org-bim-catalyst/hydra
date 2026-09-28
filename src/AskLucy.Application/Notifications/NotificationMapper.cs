using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>
/// Maps a <see cref="Notification"/> to the shape the center, its detail view, and the SignalR push
/// all share (<see cref="NotificationListItemDto"/> — INotificationRealtimePublisher already defines
/// it, so queries and the publisher reuse one DTO instead of each declaring their own).
/// </summary>
public static class NotificationMapper
{
    public static NotificationListItemDto ToListItemDto(Notification notification, IReadOnlyDictionary<string, IReadOnlySet<string>> availableByType)
    {
        NotificationRelatedItemDto? relatedItem = null;
        if (notification.RelatedItemType is { } type && notification.RelatedItemId is { } id)
        {
            // A type absent from the map has no registered INotificationAccessCheck yet (T061):
            // report it available rather than guessing it's gone.
            var available = !availableByType.TryGetValue(type, out var ids) || ids.Contains(id);
            relatedItem = new NotificationRelatedItemDto(type, id, available);
        }

        var action = notification.ActionRoute is { } route ? new NotificationActionDto(notification.ActionLabel, route) : null;

        return new NotificationListItemDto(
            notification.Id,
            notification.Category,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.Priority,
            notification.Status,
            notification.Language,
            notification.CreatedAtUtc,
            notification.ReadAtUtc,
            notification.ExpiresAtUtc,
            action,
            relatedItem);
    }
}
