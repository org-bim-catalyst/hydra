using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.UpdateNotificationPreferences;

/// <summary>contracts/notifications-api.md PUT /users/me/notification-preferences. Atomic: every change applies, or none does.</summary>
public sealed record UpdateNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceChange> Changes)
    : IRequest<NotificationPreferencesDto>;

/// <param name="Frequency">Optional; only <see cref="DeliveryFrequency.Immediate"/> is accepted for now.</param>
public sealed record NotificationPreferenceChange(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool Enabled,
    DeliveryFrequency? Frequency = null);
