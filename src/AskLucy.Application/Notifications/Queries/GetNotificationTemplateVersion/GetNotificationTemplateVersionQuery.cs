using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplateVersion;

/// <summary>contracts/admin-notifications-api.md GET /notifications/templates/{templateId}/versions/{versionId} (V).</summary>
public sealed record GetNotificationTemplateVersionQuery(Guid TemplateId, Guid VersionId) : IRequest<TemplateVersionDto>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.TemplateVersionViewed;

    public string AuditTargetType => "NotificationTemplateVersion";

    public string AuditTargetId => VersionId.ToString();
}
