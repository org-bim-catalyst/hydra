using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

public enum NotificationAuditAction
{
    TemplateDraftSaved,
    TemplateVersionPublished,
    TemplateVersionArchived,
    DeliveryRetried,
    DeliveriesBulkRetried,
    AnnouncementPublished,
    LocalizationSettingChanged,
    TemplateTestSent,
    ApprovalNotificationCreated,
    ApprovalNotificationDelivered,
    ApprovalNotificationRead,
}

public enum NotificationAuditOutcome
{
    Succeeded,
    Rejected,
    Failed,
}

/// <summary>
/// Append-only audit of hub administration and approval-notification history (FR-037, FR-054,
/// SC-008). Mirrors <see cref="Authorization.RoleAuditLog"/>: no update or delete paths, and no FK to
/// the actor, so an entry outlives a removed user. Retention never deletes these rows.
/// </summary>
public sealed class NotificationAuditLog : BaseEntity, ICorrelated
{
    public const int TargetTypeMaxLength = 60;
    public const int TargetIdMaxLength = 100;

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Null for system-generated approval-history rows.</summary>
    public string? ActorUserId { get; private set; }

    public NotificationAuditAction Action { get; private set; }

    public string TargetType { get; private set; } = string.Empty;

    public string TargetId { get; private set; } = string.Empty;

    public NotificationAuditOutcome Outcome { get; private set; }

    /// <summary>A safe before/after summary; never a token, credential or rendered email.</summary>
    public string? DetailsJson { get; private set; }

    /// <summary>Set by the caller from the ambient request or job; otherwise stamped by the SaveChanges audit interceptor.</summary>
    public string? CorrelationId { get; private set; }

    private NotificationAuditLog()
    {
        // Required by EF Core materialization.
    }

    /// <summary>Creates an audit entry. Use this factory method exclusively.</summary>
    public static NotificationAuditLog Record(
        NotificationAuditAction action,
        string? actorUserId,
        string targetType,
        string targetId,
        NotificationAuditOutcome outcome,
        DateTime now,
        string? detailsJson = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (targetType.Length > TargetTypeMaxLength || targetId.Length > TargetIdMaxLength)
        {
            throw new DomainRuleViolationException("The audit target is too long.");
        }

        var actor = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        return new NotificationAuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAtUtc = now,
            ActorUserId = actor,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = outcome,
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? null : detailsJson,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
            CreatedAtUtc = now,
            CreatedBy = actor ?? "system",
        };
    }
}
