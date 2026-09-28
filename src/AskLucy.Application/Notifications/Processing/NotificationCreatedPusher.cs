using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>
/// Pushes <c>notificationCreated</c> for committed notifications. Call it only after the save:
/// pushing first could announce a row that then rolls back. Best-effort: a failed push is logged
/// at Warning and not retried, because the client refetches on reconnect (contracts/notification-hub.md).
/// </summary>
public sealed class NotificationCreatedPusher(
    INotificationRepository notifications,
    INotificationRealtimePublisher realtime,
    ILogger<NotificationCreatedPusher> logger)
{
    public async Task PushAsync(IReadOnlyCollection<Notification> committed, CancellationToken cancellationToken)
    {
        var visible = committed.Where(n => n.ShowInCenter && n.RecipientUserId is not null).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, int> unread;
        try
        {
            unread = await notifications.CountUnreadAsync([.. visible.Select(n => n.RecipientUserId!).Distinct()], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NotificationDispatchLog.UnreadCountFailed(logger, ex, visible[0].CorrelationId, visible.Count);
            return;
        }

        foreach (var notification in visible)
        {
            var userId = notification.RecipientUserId!;
            try
            {
                await realtime.NotificationCreatedAsync(userId, ToListItem(notification), unread.GetValueOrDefault(userId), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                NotificationDispatchLog.PushFailed(logger, ex, notification.CorrelationId, notification.Id);
            }
        }
    }

    /// <summary>
    /// The push shape. A just-created item's related item is available by construction: the
    /// dispatcher re-checked access moments ago (R24).
    /// </summary>
    public static NotificationListItemDto ToListItem(Notification n) => new(
        n.Id,
        n.Category,
        n.Type,
        n.Title,
        n.Message,
        n.Priority,
        n.Status,
        n.Language,
        n.CreatedAtUtc,
        n.ReadAtUtc,
        n.ExpiresAtUtc,
        n.ActionRoute is null ? null : new NotificationActionDto(n.ActionLabel, n.ActionRoute),
        n is { RelatedItemType: { } type, RelatedItemId: { } id } ? new NotificationRelatedItemDto(type, id, Available: true) : null);
}
