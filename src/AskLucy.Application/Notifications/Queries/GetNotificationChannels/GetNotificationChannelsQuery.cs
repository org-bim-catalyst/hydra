using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationChannels;

/// <summary>contracts/admin-notifications-api.md GET /notifications/channels.</summary>
public sealed record GetNotificationChannelsQuery : IRequest<IReadOnlyList<NotificationChannelDto>>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.ChannelsViewed;

    public string AuditTargetType => "NotificationChannels";

    public string AuditTargetId => "all";
}
