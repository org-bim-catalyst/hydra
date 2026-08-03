using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetUserVoicePreference;

public sealed class GetUserVoicePreferenceQueryHandler(
    IUserVoicePreferenceRepository preferences,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUserVoicePreferenceQuery, UserVoicePreferenceDto>
{
    public async Task<UserVoicePreferenceDto> Handle(GetUserVoicePreferenceQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        // No saved row yet (data-model.md — created lazily on first save): return the
        // platform default (Push-to-Talk, unmuted, no overrides) rather than 404/null,
        // matching contracts/voice-preferences.md's documented GET behavior.
        if (preference is null)
        {
            return new UserVoicePreferenceDto(VoiceConversationMode.PushToTalk.ToString(), IsMuted: false, null, null, null, null, null);
        }

        return new UserVoicePreferenceDto(
            preference.ConversationMode.ToString(), preference.IsMuted, preference.SelectedVoiceId,
            preference.VoiceSpeed, preference.VoiceStyle, preference.PreferredMicrophoneDeviceId, preference.PreferredSpeakerDeviceId);
    }
}
