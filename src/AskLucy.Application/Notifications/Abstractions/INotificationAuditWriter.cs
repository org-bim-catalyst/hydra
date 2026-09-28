using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Adds append-only audit rows (FR-054) to the current unit of work; the caller's save commits them.</summary>
public interface INotificationAuditWriter
{
    /// <param name="action">What happened.</param>
    /// <param name="targetType">The kind of thing acted on, e.g. <c>NotificationTemplate</c>.</param>
    /// <param name="targetId">The id of the thing acted on.</param>
    /// <param name="outcome">Whether the action succeeded.</param>
    /// <param name="details">Serialized to JSON; must carry no secrets or message bodies.</param>
    void Write(
        NotificationAuditAction action,
        string targetType,
        string targetId,
        NotificationAuditOutcome outcome,
        object? details = null);
}
