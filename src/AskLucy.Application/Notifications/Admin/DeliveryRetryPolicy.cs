using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Admin;

/// <summary>The checks an administrator retry makes before it resets a delivery (spec edge cases), shared by the single and the bulk retry.</summary>
internal sealed class DeliveryRetryPolicy(INotificationRecipientDirectory directory)
{
    /// <summary>Why <paramref name="delivery"/> of <paramref name="notification"/> can't be retried, or null when it can.</summary>
    public async Task<DeliveryRetryRefusal?> RefusalAsync(
        Notification notification, NotificationDelivery delivery, DateTime now, CancellationToken cancellationToken)
    {
        if (delivery.Status is not (DeliveryStatus.Failed or DeliveryStatus.DeadLettered))
        {
            return DeliveryRetryRefusal.NotFailed;
        }

        if (notification.DeletedAtUtc is not null)
        {
            return DeliveryRetryRefusal.NotificationDeleted;
        }

        if (notification.ExpiresAtUtc is { } expires && expires <= now)
        {
            return DeliveryRetryRefusal.NotificationExpired;
        }

        if (delivery.RecipientKind == RecipientKind.User && notification.RecipientUserId is { } userId)
        {
            var accounts = await directory.GetAsync([userId], cancellationToken);
            if (!accounts.TryGetValue(userId, out var account) || !account.IsActive)
            {
                return DeliveryRetryRefusal.RecipientDeleted;
            }
        }

        return null;
    }
}
