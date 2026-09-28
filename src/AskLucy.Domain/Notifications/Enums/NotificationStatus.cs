namespace AskLucy.Domain.Notifications;

/// <summary>FR-011 lifecycle, aggregated from the notification's deliveries (research R8).</summary>
public enum NotificationStatus
{
    Created,
    Queued,
    Processing,
    Sent,
    Delivered,
    Read,
    Failed,
    Cancelled,
    Expired,
}
