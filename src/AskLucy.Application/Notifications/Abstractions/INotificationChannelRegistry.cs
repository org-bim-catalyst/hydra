using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// The channels the router may use right now: in-app when enabled, plus every channel with a
/// registered sender that configuration hasn't disabled (FR-060). A channel outside this set is
/// skipped as disabled, never queued.
/// </summary>
public interface INotificationChannelRegistry
{
    IReadOnlySet<NotificationChannel> AvailableChannels { get; }
}
