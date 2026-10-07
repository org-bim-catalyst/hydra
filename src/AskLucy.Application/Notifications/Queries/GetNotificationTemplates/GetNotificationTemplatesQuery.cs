using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplates;

/// <summary>contracts/admin-notifications-api.md GET /notifications/templates (V).</summary>
public sealed record GetNotificationTemplatesQuery(
    NotificationCategory? Category = null,
    NotificationChannel? Channel = null,
    string? Language = null,
    string? Type = null) : IRequest<IReadOnlyList<NotificationTemplateSummaryDto>>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.TemplatesViewed;

    public string AuditTargetType => "NotificationTemplate";

    public string AuditTargetId => "all";
}
