using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Queries.GetNotificationChannels;

public sealed class GetNotificationChannelsQueryHandler(
    INotificationChannelRegistry registry,
    INotificationChannelHealthReader healthReader,
    IOptions<NotificationsOptions> options)
    : IRequestHandler<GetNotificationChannelsQuery, IReadOnlyList<NotificationChannelDto>>
{
    public async Task<IReadOnlyList<NotificationChannelDto>> Handle(GetNotificationChannelsQuery request, CancellationToken cancellationToken)
    {
        var channels = new List<NotificationChannelDto>();
        foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.InApp })
        {
            var health = await healthReader.GetAsync(channel, cancellationToken);
            channels.Add(new NotificationChannelDto(
                channel,
                registry.AvailableChannels.Contains(channel),
                channel == NotificationChannel.Email ? "SMTP" : "SignalR",
                health.Health,
                health.CheckedAtUtc,
                health.Detail,
                channel == NotificationChannel.Email ? Math.Max(1, options.Value.Email.MaxPerMinute) : null));
        }

        return channels;
    }
}
