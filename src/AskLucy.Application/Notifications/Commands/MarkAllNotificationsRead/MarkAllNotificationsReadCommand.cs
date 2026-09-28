using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>contracts/notifications-api.md POST /notifications/actions/mark-all-read. Null category means every category.</summary>
public sealed record MarkAllNotificationsReadCommand(NotificationCategory? Category = null) : IRequest<int>;
