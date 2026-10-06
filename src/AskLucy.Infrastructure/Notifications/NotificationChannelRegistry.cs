using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// A channel that has a registered <see cref="INotificationChannelSender"/>. Registered as a singleton
/// next to each scoped sender, because the channel registry is a singleton and can't resolve a scoped
/// service to ask it which channel it serves.
/// </summary>
public sealed record RegisteredChannelSender(NotificationChannel Channel);

/// <summary>
/// The channels routing may use (FR-060): in-app unless configuration turns it off, plus every channel
/// with a registered sender that configuration hasn't disabled. A channel with no sender, or one that is
/// switched off, is recorded as skipped for a disabled channel rather than queued with nothing to send it.
/// </summary>
public sealed class NotificationChannelRegistry(
    IOptionsMonitor<NotificationsOptions> options,
    IEnumerable<RegisteredChannelSender> senders) : INotificationChannelRegistry
{
    private readonly NotificationChannel[] _senderChannels = [.. senders.Select(s => s.Channel).Distinct()];

    public IReadOnlySet<NotificationChannel> AvailableChannels
    {
        get
        {
            var toggles = options.CurrentValue.Channels;
            var channels = new HashSet<NotificationChannel>();
            if (toggles.InApp.Enabled)
            {
                channels.Add(NotificationChannel.InApp);
            }

            foreach (var channel in _senderChannels)
            {
                // Only the channels with a configuration toggle can be switched off; a channel added
                // later is on once its sender is registered (FR-060).
                var enabled = channel switch
                {
                    NotificationChannel.Email => toggles.Email.Enabled,
                    _ => true,
                };

                if (enabled)
                {
                    channels.Add(channel);
                }
            }

            return channels;
        }
    }
}
