using AskLucy.Application.Abstractions;
using AskLucy.Domain.Panels;
using MediatR;

namespace AskLucy.Application.Panels.Commands.SaveUserPanelPreference;

/// <summary>data-model.md: <see cref="UserPanelPreference"/> is created lazily on first save, not at registration.</summary>
public sealed class SaveUserPanelPreferenceCommandHandler(
    IUserPanelPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SaveUserPanelPreferenceCommand, UserPanelPreferenceDto>
{
    public async Task<UserPanelPreferenceDto> Handle(SaveUserPanelPreferenceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        if (preference is null)
        {
            preference = UserPanelPreference.Create(userId, userId);
            preferences.Add(preference);
        }

        preference.SetOpacityPercent(request.OpacityPercent, userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserPanelPreferenceDto(preference.OpacityPercent);
    }
}
