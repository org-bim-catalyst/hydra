using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>The append-only notification audit trail (FR-054). Rows are never updated or deleted.</summary>
public interface INotificationAuditLogRepository
{
    void Add(NotificationAuditLog entry);

    /// <summary>Whether <paramref name="actorUserId"/> already has a <paramref name="action"/> row for the target since <paramref name="sinceUtc"/>.</summary>
    Task<bool> ExistsSinceAsync(
        NotificationAuditAction action,
        string actorUserId,
        string targetType,
        string targetId,
        DateTime sinceUtc,
        CancellationToken cancellationToken);
}
