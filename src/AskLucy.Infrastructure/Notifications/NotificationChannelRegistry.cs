using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// The channels routing may use (FR-060): in-app unless configuration turns it off. Email joins
/// once its sender is registered (US3); until then an email delivery is recorded as skipped for a
/// disabled channel rather than queued with nothing to send it.
/// </summary>
public sealed class NotificationChannelRegistry(IOptionsMonitor<NotificationsOptions> options) : INotificationChannelRegistry
{
    public IReadOnlySet<NotificationChannel> AvailableChannels
    {
        get
        {
            var channels = new HashSet<NotificationChannel>();
            if (options.CurrentValue.Channels.InApp.Enabled)
            {
                channels.Add(NotificationChannel.InApp);
            }

            return channels;
        }
    }
}
