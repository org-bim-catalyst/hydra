using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Port for pushing memory-related events to the owning user in near-real-time and for
/// creating/delivering in-app notifications (spec.md FR-006a, research.md Decision 11) — mirrors
/// <see cref="IProcessingNotifier"/>'s shape exactly. The <c>MemoryHub</c> SignalR hub is the
/// concrete delivery mechanism, implemented in <c>Infrastructure</c> (Application/Domain never
/// reference SignalR directly, constitution §3).
/// </summary>
public interface IMemoryNotifier
{
    /// <summary>
    /// Maps <paramref name="eventType"/> to its <c>memory.*</c> catalogue key (specs/067
    /// data-model.md § Legacy mapping) and publishes it through <c>INotificationPublisher</c>
    /// (T085) — no longer writes a <see cref="MemoryNotification"/> row.
    /// </summary>
    Task NotifyAsync(string userId, Guid? memoryId, MemoryNotificationEventType eventType, string message, CancellationToken cancellationToken = default);
}
