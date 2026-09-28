namespace AskLucy.Domain.Notifications;

/// <summary>Per-channel delivery state (FR-012), independent of other channels.</summary>
public enum DeliveryStatus
{
    Pending,
    Sending,
    Retrying,
    Sent,
    Delivered,
    Skipped,
    Failed,
    DeadLettered,
    Cancelled,
    Expired,
}
