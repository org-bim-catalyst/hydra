namespace AskLucy.Application.Abstractions;

/// <summary>A single-use, short-lived token the browser uses to connect directly to the
/// primary voice provider's realtime speech-to-text endpoint (research.md Decision 2) —
/// the raw provider credential never leaves the backend.</summary>
public sealed record SpeechToTextSession(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// The primary speech-to-text provider abstraction (constitution §9), mirroring
/// <see cref="ITranscriptionProvider"/>'s separation from <see cref="IAIProvider"/> — this
/// covers the streaming realtime path specifically, not the legacy batch/fallback path.
/// One implementation today (<c>ElevenLabsSpeechToTextSessionProvider</c>), swappable per
/// spec's "future support for additional speech providers" goal.
/// </summary>
public interface ISpeechToTextSessionProvider
{
    /// <summary><paramref name="language"/> hints the provider's transcription language
    /// (research.md Decision 9) — the same value already threaded through the legacy TTS
    /// path in <c>ChatPage.tsx</c>.</summary>
    Task<SpeechToTextSession> CreateSessionAsync(string language, CancellationToken cancellationToken = default);
}
