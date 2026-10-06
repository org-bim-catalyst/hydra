using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationPreferences;

/// <summary>contracts/notifications-api.md GET /users/me/notification-preferences.</summary>
public sealed record GetNotificationPreferencesQuery : IRequest<NotificationPreferencesDto>;
