using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationAudit;

/// <summary>contracts/admin-notifications-api.md GET /notifications/audit. Read-only: no endpoint changes or deletes an audit row.</summary>
public sealed record GetNotificationAuditQuery(
    NotificationAuditAction? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    string? ActorUserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Cursor = null,
    int Limit = 50)
    : IRequest<AdminPage<AdminAuditEntryDto>>, IAuditedAdminView
{
    public const int MaxLimit = 200;

    public NotificationAuditAction AuditAction => NotificationAuditAction.AuditViewed;

    public string AuditTargetType => "NotificationAudit";

    public string AuditTargetId => "all";
}
