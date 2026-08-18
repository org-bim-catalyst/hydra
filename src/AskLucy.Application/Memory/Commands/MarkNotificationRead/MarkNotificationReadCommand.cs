using MediatR;

namespace AskLucy.Application.Memory.Commands.MarkNotificationRead;

/// <summary>contracts/memory-privacy-api.md — `POST /api/v1/memories/notifications/{id}/actions/mark-read`.</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest;
