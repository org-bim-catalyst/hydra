using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>Stamps each audit row with the acting user (null for the system) and the ambient correlation id.</summary>
public sealed class NotificationAuditWriter(
    INotificationAuditLogRepository auditLogs,
    ICurrentUserAccessor currentUser,
    ICorrelationIdAccessor correlation,
    TimeProvider timeProvider) : INotificationAuditWriter
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public void Write(
        NotificationAuditAction action,
        string targetType,
        string targetId,
        NotificationAuditOutcome outcome,
        object? details = null,
        string? correlationId = null)
    {
        var entry = NotificationAuditLog.Record(
            action,
            currentUser.UserId,
            targetType,
            targetId,
            outcome,
            timeProvider.GetUtcNow().UtcDateTime,
            details is null ? null : JsonSerializer.Serialize(details, DetailsJson),
            correlationId ?? correlation.Current);

        auditLogs.Add(entry);
    }
}
