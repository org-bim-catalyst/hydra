using MediatR;

namespace AskLucy.Application.Notifications.Commands.DeleteNotification;

/// <summary>contracts/notifications-api.md DELETE /notifications/{id} — owner soft delete (FR-016a).</summary>
public sealed record DeleteNotificationCommand(Guid NotificationId) : IRequest;
