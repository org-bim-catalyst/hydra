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
