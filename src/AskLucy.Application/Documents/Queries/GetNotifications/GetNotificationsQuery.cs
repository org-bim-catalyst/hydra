using MediatR;

namespace AskLucy.Application.Documents.Queries.GetNotifications;

public sealed record GetNotificationsQuery(bool UnreadOnly, string? Cursor, int PageSize) : IRequest<DocumentNotificationPageDto>;
