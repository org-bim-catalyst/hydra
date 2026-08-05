using MediatR;

namespace AskLucy.Application.Documents.Commands.MarkNotificationRead;

public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest;
