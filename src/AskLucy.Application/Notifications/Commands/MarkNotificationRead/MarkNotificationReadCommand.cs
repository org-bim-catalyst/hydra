using MediatR;

namespace AskLucy.Application.Notifications.Commands.MarkNotificationRead;

/// <summary>contracts/notifications-api.md POST /notifications/{id}/actions/mark-read. Idempotent.</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest;
