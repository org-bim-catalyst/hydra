using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplate;

/// <summary>contracts/admin-notifications-api.md GET /notifications/templates/{templateId} (V). 404 when there is no such template.</summary>
public sealed record GetNotificationTemplateQuery(Guid TemplateId) : IRequest<NotificationTemplateDetailDto>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.TemplateViewed;

    public string AuditTargetType => "NotificationTemplate";

    public string AuditTargetId => TemplateId.ToString();
}
