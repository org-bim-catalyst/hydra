namespace AskLucy.Application.Notifications.Admin;

/// <summary>An administrator tried to retry a delivery that can't be retried. Mapped to 409 with the machine-readable <see cref="Reason"/>.</summary>
public sealed class DeliveryRetryRefusedException(DeliveryRetryRefusal reason)
    : Exception($"This delivery can't be retried: {reason}.")
{
    public DeliveryRetryRefusal Reason { get; } = reason;
}
