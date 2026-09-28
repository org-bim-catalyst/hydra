using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Legacy;
using AskLucy.Domain.Memory;

namespace AskLucy.Infrastructure.Memory;

/// <summary>
/// <see cref="IMemoryNotifier"/> implementation — publishes <c>memory.auto-created</c>,
/// <c>memory.auto-approved</c> and <c>memory.conflict.confirmation-needed</c> through
/// <see cref="INotificationPublisher"/> (specs/067 T085). No longer writes a
/// <see cref="MemoryNotification"/> row or pushes <c>memoryNotificationCreated</c> — the
/// notification hub owns delivery from here on.
/// </summary>
public sealed class MemoryNotifier(INotificationPublisher publisher) : IMemoryNotifier
{
    public Task NotifyAsync(string userId, Guid? memoryId, MemoryNotificationEventType eventType, string message, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            LegacyNotificationTypeMap.ToCatalogKey(eventType),
            new NotificationRecipient.User(userId),
            new Dictionary<string, string?> { ["memorySummary"] = message },
            memoryId is { } id ? new RelatedItem("Memory", id.ToString()) : null,
            EventKey: $"memory:{memoryId?.ToString() ?? "none"}:{eventType}"));

        return Task.CompletedTask;
    }
}
