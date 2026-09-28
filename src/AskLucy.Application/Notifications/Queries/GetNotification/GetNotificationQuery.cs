using AskLucy.Application.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotification;

/// <summary>contracts/notifications-api.md GET /notifications/{id}. 404 when not found, not owned, or owner-deleted.</summary>
public sealed record GetNotificationQuery(Guid NotificationId) : IRequest<NotificationDetailDto>;
