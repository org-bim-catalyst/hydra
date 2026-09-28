using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Authorization;
using AskLucy.Application.Notifications.Processing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Commands.DeleteNotification;

public sealed class DeleteNotificationCommandHandler(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    INotificationRealtimePublisher realtime,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<DeleteNotificationCommandHandler> logger) : IRequestHandler<DeleteNotificationCommand>
{
    public async Task Handle(DeleteNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var notification = NotificationOwnershipGuard.EnsureOwnedBy(
            await notificationRepository.GetByIdAsync(request.NotificationId, cancellationToken), userId);

        notification.DeleteByOwner(userId, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Best-effort (contracts/notification-hub.md): a failed push never fails the request,
        // because the client's other sessions catch up on reconnect.
        try
        {
            var unreadCount = await notificationRepository.CountUnreadAsync(userId, cancellationToken);
            await realtime.NotificationUpdatedAsync(userId, notification.Id, NotificationChange.Deleted, unreadCount, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NotificationDispatchLog.UpdatePushFailed(logger, ex, notification.Id, NotificationChange.Deleted);
        }
    }
}
