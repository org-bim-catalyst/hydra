using AskLucy.Domain.Notifications;

namespace AskLucy.Web.Contracts;

/// <summary>contracts/notifications-api.md `GET /notifications/unread-count`.</summary>
public sealed record UnreadNotificationCountResponse(int Count);

/// <summary>contracts/notifications-api.md `POST /notifications/actions/mark-all-read`. Omitted means every category.</summary>
public sealed record MarkAllNotificationsReadRequest(NotificationCategory? Category = null);

/// <summary>contracts/notifications-api.md `POST /notifications/actions/mark-all-read`.</summary>
public sealed record MarkAllNotificationsReadResponse(int Updated);

/// <summary>contracts/notifications-api.md `PUT /users/me/notification-preferences`.</summary>
public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceChangeRequest>? Changes);

/// <param name="Frequency">Optional; only <c>Immediate</c> is accepted.</param>
public sealed record NotificationPreferenceChangeRequest(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool Enabled,
    DeliveryFrequency? Frequency = null);

/// <summary>contracts/admin-notifications-api.md `POST /notifications/deliveries/actions/retry`: exactly one of the two.</summary>
public sealed record BulkRetryDeliveriesRequest(IReadOnlyList<Guid>? DeliveryIds = null, BulkRetryDeliveriesFilterRequest? Filter = null);

public sealed record BulkRetryDeliveriesFilterRequest(
    IReadOnlyList<DeliveryStatus>? Status = null,
    NotificationChannel? Channel = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>contracts/admin-notifications-api.md `POST /notifications/deliveries/{deliveryId}/actions/retry`.</summary>
public sealed record RetryDeliveryResponse(Guid DeliveryId, DeliveryStatus Status);

/// <summary>contracts/admin-notifications-api.md `POST /notifications/announcements`.</summary>
public sealed record PublishAnnouncementRequest(
    AnnouncementKind Kind,
    string Title,
    string Message,
    AnnouncementAudience Audience,
    IReadOnlyList<string>? TargetRoleIds,
    bool IsCritical,
    DateTime? EndsAtUtc);

/// <summary>
/// contracts/admin-notifications-api.md template version fields: the email fields for an email template, title and message for an
/// in-app one. <c>CopyFromVersionId</c> (create only) starts the draft from an existing version when no field is sent.
/// </summary>
public sealed record TemplateVersionRequest(
    string? Subject = null,
    string? Preheader = null,
    string? Greeting = null,
    string? Heading = null,
    IReadOnlyList<string>? BodyParagraphs = null,
    string? ActionLabel = null,
    string? SafetyNote = null,
    string? FooterNote = null,
    string? Title = null,
    string? Message = null,
    Guid? CopyFromVersionId = null);

/// <summary>contracts/admin-notifications-api.md `POST …/actions/preview`. The body is optional.</summary>
public sealed record PreviewTemplateRequest(IReadOnlyDictionary<string, string?>? Variables = null);
