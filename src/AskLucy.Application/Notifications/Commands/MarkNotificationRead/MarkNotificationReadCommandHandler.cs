using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Authorization;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Commands.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    INotificationAuditWriter audit,
    INotificationRealtimePublisher realtime,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<MarkNotificationReadCommandHandler> logger) : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var notification = NotificationOwnershipGuard.EnsureOwnedBy(
            await notificationRepository.GetByIdAsync(request.NotificationId, cancellationToken), userId);

        var wasUnread = !notification.IsRead;
        notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);

        // FR-054: the first time an approval request is opened is part of its audit trail.
        if (wasUnread && NotificationTypeCatalog.TryGet(notification.Type, out var definition) && definition!.IsApproval)
        {
            audit.Write(
                NotificationAuditAction.ApprovalNotificationRead,
                nameof(Notification),
                notification.Id.ToString(),
                NotificationAuditOutcome.Succeeded,
                new { type = definition.Key },
                notification.CorrelationId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!wasUnread)
        {
            // Idempotent per the contract: no state changed, so no push either.
            return;
        }

        // Best-effort (contracts/notification-hub.md): a failed push never fails the request,
        // because the client's other sessions catch up on reconnect.
        try
        {
            var unreadCount = await notificationRepository.CountUnreadAsync(userId, cancellationToken);
            await realtime.NotificationUpdatedAsync(userId, notification.Id, NotificationChange.Read, unreadCount, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NotificationDispatchLog.UpdatePushFailed(logger, ex, notification.Id, NotificationChange.Read);
        }
    }
}
