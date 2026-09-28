using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// The hub's OpenTelemetry instruments (meter <c>AskLucy.Notifications</c>, FR-057). Recording is
/// in-memory and never throws, so callers need no error handling around it.
/// </summary>
public interface INotificationMetrics
{
    void NotificationCreated(NotificationCategory category, string type);

    void DeliverySent(NotificationChannel channel, TimeSpan latency);

    void DeliveryFailed(NotificationChannel channel, DeliveryFailureKind failureKind);

    void DeliveryRetried(NotificationChannel channel);

    void DeliveryDeadLettered(NotificationChannel channel);

    void ProviderError(NotificationChannel channel, DeliveryFailureKind failureKind);
}
