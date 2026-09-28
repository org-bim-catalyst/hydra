using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// Marks an admin query whose result exposes delivery data (contracts/admin-notifications-api.md).
/// <see cref="Behaviors.AdminViewAuditBehavior{TRequest, TResponse}"/> audits it after the handler
/// succeeds, so the handler itself stays read-only (constitution §3).
/// </summary>
public interface IAuditedAdminView
{
    NotificationAuditAction AuditAction { get; }

    string AuditTargetType { get; }

    /// <summary>The viewed resource's id, or a stable name such as <c>all</c> for a list view.</summary>
    string AuditTargetId { get; }
}
