using AskLucy.Application.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// The text-to-speech abstraction every voice-output handler speaks through (constitution §9).
/// Implemented by <c>VoiceProviderRouter</c> (specs/070), which fronts the administrator-ordered
/// <see cref="ITextToSpeechEngine"/> set and fails over between engines. Yields raw audio byte
/// chunks as they arrive from the engine — callers relay them to the client without buffering the
/// full reply first (FR-008/FR-026).
/// </summary>
public interface ITextToSpeechProvider
{
    IAsyncEnumerable<byte[]> StreamSpeechAsync(string textChunk, VoiceSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>Resolves the platform-wide default voice/model/synthesis settings for a
    /// language, before any <c>UserVoicePreference</c> override is applied (research.md
    /// Decision 9). Keeps the per-language/platform-default cascade entirely behind this
    /// abstraction so Application code never references Infrastructure's provider options
    /// directly (constitution §3). Async since specs/070: the primary engine is read from the
    /// database.</summary>
    Task<VoiceSettingsDto> ResolveDefaultSettingsAsync(string language, CancellationToken cancellationToken = default);
}
