namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>Which engine the browser dictates with for this turn.</summary>
public enum DictationEngine
{
    /// <summary>The realtime provider's browser-direct WebSocket, using <see cref="DictationSession.Token"/>.</summary>
    Realtime,

    /// <summary>
    /// The utterance is recorded and posted to <c>/api/v1/ai/voice/transcriptions</c> once the
    /// user pauses. The server routes it to whichever clip engine (Local Whisper or OpenAI
    /// Whisper) resolved for this turn (specs/078 contracts/dictation-session.md).
    /// </summary>
    Clip,

    /// <summary>
    /// The browser's own <c>SpeechRecognition</c> serves this turn. No vendor call is made.
    /// <see cref="DictationSession.Degraded"/> says whether this is the normal path (a fresh
    /// deployment, no model selected, the Push-to-Talk engine is Browser) or a fallback from a
    /// failure.
    /// </summary>
    Browser,
}

/// <summary>
/// contracts/dictation-session.md — the answer to "how should this turn be dictated?". The server
/// decides, so the browser never has to discover an administrator's choice by being refused.
/// </summary>
public sealed record DictationSession(DictationEngine Engine, string? Token, DateTime? ExpiresAtUtc, bool Degraded = false)
{
    public static DictationSession Realtime(string token, DateTime expiresAtUtc) =>
        new(DictationEngine.Realtime, token, expiresAtUtc);

    public static readonly DictationSession Clip = new(DictationEngine.Clip, null, null);

    public static DictationSession Browser(bool degraded) => new(DictationEngine.Browser, null, null, degraded);
}
