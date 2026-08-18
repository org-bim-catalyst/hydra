using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Memory.Queries.ListMemoryNotifications;

public sealed class ListMemoryNotificationsQueryHandler(
    IMemoryNotificationRepository notificationRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListMemoryNotificationsQuery, PagedResult<MemoryNotificationDto>>
{
    public async Task<PagedResult<MemoryNotificationDto>> Handle(ListMemoryNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var afterId = Guid.TryParse(request.Cursor, out var parsed) ? parsed : (Guid?)null;

        var notifications = await notificationRepository.GetByUserAsync(userId, afterId, request.PageSize, cancellationToken);

        var items = notifications
            .Select(n => new MemoryNotificationDto(n.Id, n.MemoryId, n.EventType.ToString(), n.Message, n.CreatedAtUtc, n.ReadAtUtc))
            .ToList();
        var nextCursor = items.Count == request.PageSize ? items[^1].Id.ToString() : null;

        return new PagedResult<MemoryNotificationDto>(items, nextCursor);
    }
}
