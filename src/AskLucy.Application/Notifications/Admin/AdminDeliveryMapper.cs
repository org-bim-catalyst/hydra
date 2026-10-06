using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Admin;

internal static class AdminDeliveryMapper
{
    public static AdminDeliveryDto ToDto(AdminDeliveryRow row, DateTime now)
    {
        var refusal = RetryRefusal(row, now);
        return new AdminDeliveryDto(
            row.DeliveryId,
            row.NotificationId,
            row.Type,
            row.Category,
            row.Channel,
            row.Status,
            row.FailureKind,
            row.FailureReason,
            row.ProviderResponse,
            row.AttemptCount,
            row.LastAttemptAtUtc,
            row.NextAttemptAtUtc,
            Recipient(row),
            row.CorrelationId,
            refusal is null,
            refusal);
    }

    public static AdminDeliveryDetailDto ToDetailDto(AdminDeliveryRow row, DateTime now) =>
        new(
            ToDto(row, now),
            new AdminDeliveryNotificationDto(
                // Security and account mail is sensitive: nothing of its content is shown (FR-056).
                row.Category is NotificationCategory.Security or NotificationCategory.Account ? null : row.Title,
                row.NotificationCreatedAtUtc,
                row.Language,
                row.TemplateVersionId));

    /// <summary>Why a retry would be refused, or null when it is allowed (contracts/admin-notifications-api.md).</summary>
    public static DeliveryRetryRefusal? RetryRefusal(AdminDeliveryRow row, DateTime now)
    {
        if (row.Status is not (DeliveryStatus.Failed or DeliveryStatus.DeadLettered))
        {
            return DeliveryRetryRefusal.NotFailed;
        }

        if (row.NotificationDeleted)
        {
            return DeliveryRetryRefusal.NotificationDeleted;
        }

        if (row.NotificationExpiresAtUtc is { } expires && expires <= now)
        {
            return DeliveryRetryRefusal.NotificationExpired;
        }

        return row.RecipientKind == RecipientKind.User && row.RecipientDeleted ? DeliveryRetryRefusal.RecipientDeleted : null;
    }

    private static AdminRecipientDto Recipient(AdminDeliveryRow row) => row.RecipientKind switch
    {
        // The support mailbox comes from server configuration and is never shown.
        RecipientKind.SupportMailbox => new AdminRecipientDto(row.RecipientKind, null, null, null),
        RecipientKind.User => new AdminRecipientDto(row.RecipientKind, row.RecipientUserId, AdminAddressMask.Initials(row.RecipientDisplayName), AdminAddressMask.Mask(row.RecipientEmail)),
        _ => new AdminRecipientDto(row.RecipientKind, row.RecipientUserId, AdminAddressMask.Initials(row.RecipientDisplayName), AdminAddressMask.Mask(row.RecipientEmail)),
    };
}
