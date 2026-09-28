using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>
/// A set-based update (contracts/notifications-api.md: "a set-based update of the caller's rows
/// only"), so it bypasses <see cref="IUnitOfWork"/> entirely rather than routing through the change
/// tracker for potentially many rows.
/// </summary>
public sealed class MarkAllNotificationsReadCommandHandler(
    INotificationRepository notificationRepository,
    INotificationRealtimePublisher realtime,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<MarkAllNotificationsReadCommandHandler> logger) : IRequestHandler<MarkAllNotificationsReadCommand, int>
{
    public async Task<int> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var updated = await notificationRepository.MarkAllReadAsync(
            userId, request.Category, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (updated == 0)
        {
            return updated;
        }

        // Best-effort (contracts/notification-hub.md): a failed push never fails the request,
        // because the client's other sessions catch up on reconnect.
        try
        {
            var unreadCount = await notificationRepository.CountUnreadAsync(userId, cancellationToken);
            await realtime.UnreadCountChangedAsync(userId, unreadCount, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NotificationDispatchLog.UnreadCountPushFailed(logger, ex, userId);
        }

        return updated;
    }
}
