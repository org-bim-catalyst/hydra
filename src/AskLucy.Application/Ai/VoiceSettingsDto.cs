namespace AskLucy.Application.Ai;

/// <summary>The resolved voice/synthesis settings for one TTS call — already cascaded from
/// <c>UserVoicePreference</c> down to the engine's per-language and platform defaults
/// (research.md Decision 9) by the time an engine sees it. The ElevenLabs-shaped tuning fields are
/// ignored by engines that have no equivalent. <see cref="Language"/> and
/// <see cref="ProviderKey"/> (specs/070) tell the router which engine the settings were resolved
/// for and which language to synthesize. <see cref="FallbackVoiceId"/> is the administrator's chosen
/// voice for that engine — what an engine that can tell <see cref="VoiceId"/> is not one of its own
/// voices (a user's free-text override meant for another engine) speaks with instead.</summary>
public sealed record VoiceSettingsDto(
    string VoiceId,
    string ModelId,
    double Stability,
    double SimilarityBoost,
    double Style,
    double Speed,
    bool UseSpeakerBoost,
    string OutputFormat,
    string? Language = null,
    string? ProviderKey = null,
    string? FallbackVoiceId = null);
