using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SaveUserVoicePreference;

/// <summary>contracts/voice-preferences.md `PUT /api/v1/ai/voice/preferences`.
/// <c>ConversationMode</c> is a plain string ("PushToTalk"/"Continuous") — see
/// <see cref="UserVoicePreferenceDto"/>'s doc comment for why.</summary>
public sealed record SaveUserVoicePreferenceCommand(
    string ConversationMode,
    bool IsMuted,
    string? SelectedVoiceId,
    double? VoiceSpeed,
    double? VoiceStyle,
    string? PreferredMicrophoneDeviceId,
    string? PreferredSpeakerDeviceId) : IRequest<UserVoicePreferenceDto>;
