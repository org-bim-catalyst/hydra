namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// Bound from configuration/environment — never hardcoded (constitution §8). Voice I/O
/// infrastructure config (research.md Decision 4) — deliberately not a row in the
/// admin-configurable <c>AIProvider</c> catalog from specs/005-multi-provider-ai-engine,
/// since ElevenLabs isn't a user-selectable chat-completion vendor.
/// </summary>
public sealed class ElevenLabsOptions
{
    public const string SectionName = "ElevenLabs";

    public required string ApiKey { get; init; }

    /// <summary>Platform-wide default voice id, used when a user has no
    /// <c>UserVoicePreference.SelectedVoiceId</c> override and no per-language mapping
    /// applies (research.md Decision 9).</summary>
    public string VoiceId { get; init; } = string.Empty;

    public string ModelId { get; init; } = "eleven_v3";

    public double Stability { get; init; } = 0.5;

    public double SimilarityBoost { get; init; } = 0.75;

    public double Style { get; init; }

    public double Speed { get; init; } = 1.0;

    public bool UseSpeakerBoost { get; init; } = true;

    public string OutputFormat { get; init; } = "mp3_44100_128";

    public string BaseUrl { get; init; } = "https://api.elevenlabs.io/v1/";

    /// <summary>Per-language voice id overrides (research.md Decision 9), mirroring the
    /// fallback engine's <c>voicePersonaMap.ts</c> — keyed by the same language codes
    /// <c>ChatPage.tsx</c> already uses. A language with no entry here falls back to
    /// <see cref="VoiceId"/>.</summary>
    public IReadOnlyDictionary<string, string> VoiceIdByLanguage { get; init; } =
        new Dictionary<string, string>();
}
