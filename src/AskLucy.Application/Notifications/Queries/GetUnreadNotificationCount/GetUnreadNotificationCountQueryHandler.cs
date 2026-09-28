using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetUnreadNotificationCount;

public sealed class GetUnreadNotificationCountQueryHandler(
    INotificationRepository notificationRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUnreadNotificationCountQuery, int>
{
    public Task<int> Handle(GetUnreadNotificationCountQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return notificationRepository.CountUnreadAsync(userId, cancellationToken);
    }
}
