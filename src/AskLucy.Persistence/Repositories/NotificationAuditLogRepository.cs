using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>Append-only (FR-037, FR-054): nothing here updates or deletes an entry.</summary>
public sealed class NotificationAuditLogRepository(AskLucyDbContext dbContext) : INotificationAuditLogRepository
{
    public void Add(NotificationAuditLog entry) => dbContext.NotificationAuditLogs.Add(entry);

    public Task<bool> ExistsSinceAsync(
        NotificationAuditAction action,
        string actorUserId,
        string targetType,
        string targetId,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        dbContext.NotificationAuditLogs.AnyAsync(
            a => a.Action == action
                && a.ActorUserId == actorUserId
                && a.TargetType == targetType
                && a.TargetId == targetId
                && a.OccurredAtUtc >= sinceUtc,
            cancellationToken);
}
