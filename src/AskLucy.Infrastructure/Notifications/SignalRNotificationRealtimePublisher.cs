using AskLucy.Application.Notifications.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// Sends the <see cref="NotificationHub"/> events. SignalR's JSON protocol writes enums as numbers,
/// so the payloads carry them as strings to match the REST contract the client shares.
/// </summary>
public sealed class SignalRNotificationRealtimePublisher(IHubContext<NotificationHub> hubContext) : INotificationRealtimePublisher
{
    public Task NotificationCreatedAsync(string userId, NotificationListItemDto item, int unreadCount, CancellationToken cancellationToken) =>
        Group(userId).SendAsync("notificationCreated", new NotificationCreatedPayload(
            item.Id,
            item.Category.ToString(),
            item.Type,
            item.Title,
            item.Message,
            item.Priority.ToString(),
            item.Status.ToString(),
            item.Language,
            item.CreatedAtUtc,
            item.ReadAtUtc,
            item.ExpiresAtUtc,
            item.Action,
            item.RelatedItem,
            unreadCount), cancellationToken);

    public Task NotificationUpdatedAsync(string userId, Guid notificationId, NotificationChange change, int unreadCount, CancellationToken cancellationToken) =>
        Group(userId).SendAsync("notificationUpdated", new NotificationUpdatedPayload(notificationId, change.ToString(), unreadCount), cancellationToken);

    public Task UnreadCountChangedAsync(string userId, int unreadCount, CancellationToken cancellationToken) =>
        Group(userId).SendAsync("unreadCountChanged", new UnreadCountPayload(unreadCount), cancellationToken);

    private IClientProxy Group(string userId) => hubContext.Clients.Group(NotificationHub.UserGroup(userId));

    private sealed record NotificationCreatedPayload(
        Guid Id,
        string Category,
        string Type,
        string Title,
        string Message,
        string Priority,
        string Status,
        string Language,
        DateTime CreatedAtUtc,
        DateTime? ReadAtUtc,
        DateTime? ExpiresAtUtc,
        NotificationActionDto? Action,
        NotificationRelatedItemDto? RelatedItem,
        int UnreadCount);

    private sealed record NotificationUpdatedPayload(Guid Id, string Change, int UnreadCount);

    private sealed record UnreadCountPayload(int UnreadCount);
}
