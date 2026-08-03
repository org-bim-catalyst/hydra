using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SaveUserVoicePreference;

/// <summary>data-model.md: <see cref="UserVoicePreference"/> is created lazily on first save, not at registration.</summary>
public sealed class SaveUserVoicePreferenceCommandHandler(
    IUserVoicePreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SaveUserVoicePreferenceCommand, UserVoicePreferenceDto>
{
    public async Task<UserVoicePreferenceDto> Handle(SaveUserVoicePreferenceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        if (preference is null)
        {
            preference = UserVoicePreference.Create(userId, userId);
            preferences.Add(preference);
        }

        preference.SetConversationMode(Enum.Parse<VoiceConversationMode>(request.ConversationMode), userId);
        preference.SetPreferences(
            request.IsMuted,
            request.SelectedVoiceId,
            request.VoiceSpeed,
            request.VoiceStyle,
            request.PreferredMicrophoneDeviceId,
            request.PreferredSpeakerDeviceId,
            userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserVoicePreferenceDto(
            preference.ConversationMode.ToString(), preference.IsMuted, preference.SelectedVoiceId,
            preference.VoiceSpeed, preference.VoiceStyle, preference.PreferredMicrophoneDeviceId, preference.PreferredSpeakerDeviceId);
    }
}
