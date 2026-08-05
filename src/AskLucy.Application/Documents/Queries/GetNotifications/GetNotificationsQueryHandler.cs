using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetNotifications;

public sealed class GetNotificationsQueryHandler(IDocumentNotificationRepository notificationRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetNotificationsQuery, DocumentNotificationPageDto>
{
    public async Task<DocumentNotificationPageDto> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await notificationRepository.ListForUserAsync(
            userId, request.UnreadOnly, request.Cursor, request.PageSize, cancellationToken);

        return new DocumentNotificationPageDto(items.Select(DocumentNotificationDto.FromEntity).ToList(), nextCursor);
    }
}
