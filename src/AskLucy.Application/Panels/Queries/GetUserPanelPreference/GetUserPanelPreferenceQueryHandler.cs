using AskLucy.Application.Abstractions;
using AskLucy.Domain.Panels;
using MediatR;

namespace AskLucy.Application.Panels.Queries.GetUserPanelPreference;

public sealed class GetUserPanelPreferenceQueryHandler(
    IUserPanelPreferenceRepository preferences,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUserPanelPreferenceQuery, UserPanelPreferenceDto>
{
    public async Task<UserPanelPreferenceDto> Handle(GetUserPanelPreferenceQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        // No saved row yet (data-model.md — created lazily on first save): return the platform
        // default without creating a row, matching contracts/panel-preferences-api.md's
        // documented GET behavior.
        if (preference is null)
        {
            return new UserPanelPreferenceDto(UserPanelPreference.DefaultOpacityPercent);
        }

        return new UserPanelPreferenceDto(preference.OpacityPercent);
    }
}
