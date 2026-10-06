using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Admin;

// contracts/admin-notifications-api.md. Nothing here carries a credential, a token, the support-mailbox address,
// a rendered account email or an unmasked recipient address.

public sealed record NotificationBacklogDto(int OutboxPending, int DeliveriesDue, int OldestDueAgeSeconds);

public sealed record NotificationCategoryStatDto(NotificationCategory Category, int Created, int Failed);

public sealed record NotificationSeriesBucketDto(DateTime BucketStartUtc, int Created, int Sent, int Failed);

public sealed record NotificationStatisticsDto(
    int Created,
    int Sent,
    int Failed,
    int DeadLettered,
    int Ambiguous,
    double? EmailSuccessRate,
    long? AverageDeliveryLatencyMs,
    long? P95DeliveryLatencyMs,
    int Retries,
    NotificationBacklogDto Backlog,
    int UnreadNotifications,
    IReadOnlyList<NotificationCategoryStatDto> ByCategory,
    IReadOnlyList<NotificationSeriesBucketDto> Series);

public sealed record NotificationChannelDto(
    NotificationChannel Channel,
    bool Enabled,
    string Provider,
    string Health,
    DateTime? CheckedAtUtc,
    string? Detail,
    int? SendLimitPerMinute);

public sealed record AdminRecipientDto(RecipientKind Kind, string? UserId, string? DisplayName, string? Address);

public sealed record AdminDeliveryDto(
    Guid DeliveryId,
    Guid NotificationId,
    string Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    DeliveryStatus Status,
    DeliveryFailureKind? FailureKind,
    string? FailureReason,
    string? ProviderResponse,
    int Attempts,
    DateTime? LastAttemptAtUtc,
    DateTime? NextAttemptAtUtc,
    AdminRecipientDto Recipient,
    string CorrelationId,
    bool Retryable,
    DeliveryRetryRefusal? NotRetryableReason);

public sealed record AdminDeliveryNotificationDto(string? Title, DateTime CreatedAtUtc, string Language, Guid? TemplateVersionId);

public sealed record AdminDeliveryDetailDto(AdminDeliveryDto Delivery, AdminDeliveryNotificationDto Notification);

public sealed record AdminAuditActorDto(string UserId, string? DisplayName);

public sealed record AdminAuditEntryDto(
    Guid Id,
    DateTime OccurredAtUtc,
    AdminAuditActorDto? Actor,
    NotificationAuditAction Action,
    string TargetType,
    string TargetId,
    NotificationAuditOutcome Outcome,
    string? Details,
    string? CorrelationId);

public sealed record AdminAnnouncementRoleDto(string Id, string Name);

public sealed record AdminAnnouncementDto(
    Guid Id,
    AnnouncementKind Kind,
    string Title,
    AnnouncementAudience Audience,
    IReadOnlyList<AdminAnnouncementRoleDto> TargetRoles,
    bool IsCritical,
    DateTime? EndsAtUtc,
    DateTime PublishedAtUtc,
    string PublishedBy,
    int? RecipientCount,
    string FanOutStatus,
    int EmailQueued,
    int EmailSent,
    int EmailExpired);

public sealed record PublishedAnnouncementDto(Guid Id, int EstimatedRecipients, int EmailEstimatedMinutes);

public sealed record AdminPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Why an administrator can't retry a delivery (spec edge case); the 409 <c>reason</c>.</summary>
public enum DeliveryRetryRefusal
{
    NotFailed,
    NotificationDeleted,
    NotificationExpired,
    RecipientDeleted,
}
