namespace AskLucy.Application.Ai;

/// <summary>The resolved voice/synthesis settings for one TTS call — already cascaded from
/// <c>UserVoicePreference</c> down to <c>ElevenLabsOptions</c>' per-language and platform
/// defaults (research.md Decision 9) by the time a provider implementation sees it.</summary>
public sealed record VoiceSettingsDto(
    string VoiceId,
    string ModelId,
    double Stability,
    double SimilarityBoost,
    double Style,
    double Speed,
    bool UseSpeakerBoost,
    string OutputFormat);
