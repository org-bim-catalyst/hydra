using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.RetryNotificationDelivery;

public sealed class RetryNotificationDeliveryCommandHandler(
    INotificationRepository notifications,
    INotificationRecipientDirectory directory,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<RetryNotificationDeliveryCommand, RetryNotificationDeliveryResult>
{
    public async Task<RetryNotificationDeliveryResult> Handle(RetryNotificationDeliveryCommand request, CancellationToken cancellationToken)
    {
        var notification = (await notifications.GetByDeliveryIdsAsync([request.DeliveryId], cancellationToken)).SingleOrDefault()
            ?? throw new KeyNotFoundException("Delivery not found.");
        var delivery = notification.Deliveries.Single(d => d.Id == request.DeliveryId);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (await new DeliveryRetryPolicy(directory).RefusalAsync(notification, delivery, now, cancellationToken) is { } refusal)
        {
            throw new DeliveryRetryRefusedException(refusal);
        }

        var previousStatus = delivery.Status;
        notification.RetryDelivery(delivery.Id, now);
        audit.Write(
            NotificationAuditAction.DeliveryRetried,
            "NotificationDelivery",
            delivery.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { notificationId = notification.Id, type = notification.Type, channel = delivery.Channel.ToString(), previousStatus = previousStatus.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RetryNotificationDeliveryResult(delivery.Id, delivery.Status);
    }
}
