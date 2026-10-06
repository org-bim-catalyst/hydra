using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Read models for the administrators' notification screens (specs/067 US6). Read-only: nothing here changes state.</summary>
public interface INotificationAdminRepository
{
    Task<NotificationStatisticsData> GetStatisticsAsync(DateTime fromUtc, DateTime toUtc, bool hourlyBuckets, DateTime now, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AdminDeliveryRow> Items, string? NextCursor)> ListDeliveriesAsync(
        AdminDeliveryFilter filter, string? cursor, int limit, CancellationToken cancellationToken);

    Task<AdminDeliveryRow?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="max"/> delivery ids matching <paramref name="filter"/>, newest first (bulk retry by filter).</summary>
    Task<IReadOnlyList<Guid>> FindDeliveryIdsAsync(AdminDeliveryFilter filter, int max, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AdminAuditRow> Items, string? NextCursor)> ListAuditAsync(
        AdminAuditFilter filter, string? cursor, int limit, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AdminAnnouncementRow> Items, string? NextCursor)> ListAnnouncementsAsync(
        string? cursor, int limit, CancellationToken cancellationToken);
}

public sealed record AdminDeliveryFilter(
    IReadOnlyList<DeliveryStatus> Statuses,
    NotificationChannel? Channel,
    NotificationCategory? Category,
    string? Type,
    DateTime? FromUtc,
    DateTime? ToUtc);

public sealed record AdminAuditFilter(
    NotificationAuditAction? Action,
    string? TargetType,
    string? TargetId,
    string? ActorUserId,
    DateTime? FromUtc,
    DateTime? ToUtc);

public sealed record CategoryCountRow(NotificationCategory Category, int Created, int Failed);

public sealed record SeriesRow(DateTime BucketStartUtc, int Created, int Sent, int Failed);

/// <summary>Database aggregates for a window (research R21). Failed counts every <c>Failed</c> delivery, ambiguous ones included.</summary>
public sealed record NotificationStatisticsData(
    int Created,
    int Sent,
    int Failed,
    int DeadLettered,
    int Ambiguous,
    int Retries,
    int EmailSent,
    int EmailFailed,
    long? AverageLatencyMs,
    long? P95LatencyMs,
    int OutboxPending,
    int DeliveriesDue,
    DateTime? OldestDueAtUtc,
    int UnreadNotifications,
    IReadOnlyList<CategoryCountRow> ByCategory,
    IReadOnlyList<SeriesRow> Series);

/// <summary>A delivery with what the admin list needs. The recipient address is unmasked here; the handler masks it.</summary>
public sealed record AdminDeliveryRow(
    Guid DeliveryId,
    Guid NotificationId,
    string Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    DeliveryStatus Status,
    DeliveryFailureKind? FailureKind,
    string? FailureReason,
    string? ProviderResponse,
    int AttemptCount,
    DateTime? LastAttemptAtUtc,
    DateTime? NextAttemptAtUtc,
    RecipientKind RecipientKind,
    string? RecipientUserId,
    string? RecipientDisplayName,
    string? RecipientEmail,
    bool RecipientDeleted,
    string CorrelationId,
    string Title,
    string Language,
    Guid? TemplateVersionId,
    DateTime NotificationCreatedAtUtc,
    bool NotificationDeleted,
    DateTime? NotificationExpiresAtUtc,
    DateTime? DeliveryExpiresAtUtc);

public sealed record AdminAuditRow(
    Guid Id,
    DateTime OccurredAtUtc,
    string? ActorUserId,
    string? ActorDisplayName,
    NotificationAuditAction Action,
    string TargetType,
    string TargetId,
    NotificationAuditOutcome Outcome,
    string? DetailsJson,
    string? CorrelationId);

public sealed record AdminAnnouncementRow(
    Guid Id,
    AnnouncementKind Kind,
    string Title,
    AnnouncementAudience Audience,
    IReadOnlyList<string> TargetRoleIds,
    IReadOnlyList<AdminRoleName> TargetRoles,
    bool IsCritical,
    DateTime? EndsAtUtc,
    DateTime PublishedAtUtc,
    string PublishedByUserId,
    string? PublishedByDisplayName,
    int? RecipientCount,
    bool FanOutCompleted,
    int EmailQueued,
    int EmailSent,
    int EmailExpired);

public sealed record AdminRoleName(string Id, string Name);
