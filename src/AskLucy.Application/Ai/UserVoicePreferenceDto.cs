namespace AskLucy.Application.Ai;

/// <summary>contracts/voice-preferences.md. <c>ConversationMode</c> is a plain string
/// ("PushToTalk"/"Continuous") rather than the <c>VoiceConversationMode</c> Domain enum
/// directly — this API has no global enum-to-string JSON converter configured, so an enum
/// serialized here would silently come out as a raw number, not the name the frontend
/// expects.</summary>
public sealed record UserVoicePreferenceDto(
    string ConversationMode,
    bool IsMuted,
    string? SelectedVoiceId,
    double? VoiceSpeed,
    double? VoiceStyle,
    string? PreferredMicrophoneDeviceId,
    string? PreferredSpeakerDeviceId);
