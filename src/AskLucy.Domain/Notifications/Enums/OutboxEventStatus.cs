namespace AskLucy.Domain.Notifications;

/// <summary>Outbox event lifecycle (research R2-R4).</summary>
public enum OutboxEventStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}
