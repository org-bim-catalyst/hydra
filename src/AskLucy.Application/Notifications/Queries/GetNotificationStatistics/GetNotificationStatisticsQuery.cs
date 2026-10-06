using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationStatistics;

/// <summary>contracts/admin-notifications-api.md GET /notifications/statistics. Default window: the last 7 days; at most 90.</summary>
public sealed record GetNotificationStatisticsQuery(DateTime? FromUtc = null, DateTime? ToUtc = null)
    : IRequest<NotificationStatisticsDto>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.StatisticsViewed;

    public string AuditTargetType => "NotificationStatistics";

    public string AuditTargetId => "all";
}
