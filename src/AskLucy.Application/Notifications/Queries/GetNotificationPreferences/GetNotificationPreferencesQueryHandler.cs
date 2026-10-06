using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationPreferences;

public sealed class GetNotificationPreferencesQueryHandler(
    INotificationPreferenceRepository preferences,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(GetNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return NotificationPreferencesBuilder.Build(await preferences.GetOverridesAsync(userId, cancellationToken));
    }
}
