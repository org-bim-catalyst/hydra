using AskLucy.Application.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// The primary text-to-speech provider abstraction (constitution §9). One implementation
/// today (<c>ElevenLabsTextToSpeechProvider</c>), swappable per spec's "future support for
/// additional speech providers" goal. Yields raw audio byte chunks as they arrive from the
/// provider — callers relay them to the client without buffering the full reply first
/// (FR-008/FR-026).
/// </summary>
public interface ITextToSpeechProvider
{
    IAsyncEnumerable<byte[]> StreamSpeechAsync(string textChunk, VoiceSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>Resolves the platform-wide default voice/model/synthesis settings for a
    /// language, before any <c>UserVoicePreference</c> override is applied (research.md
    /// Decision 9). Keeps the per-language/platform-default cascade entirely behind this
    /// abstraction so Application code never references Infrastructure's provider options
    /// directly (constitution §3).</summary>
    VoiceSettingsDto ResolveDefaultSettings(string language);
}
