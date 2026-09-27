namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>Which engine the browser dictates with for this turn.</summary>
public enum DictationEngine
{
    /// <summary>The realtime provider's browser-direct WebSocket, using <see cref="DictationSession.Token"/>.</summary>
    Realtime,

    /// <summary>
    /// The utterance is recorded and posted to <c>/api/v1/ai/transcriptions</c> once the user
    /// pauses. Chosen when the realtime provider is switched off under Admin → AI providers.
    /// </summary>
    Whisper,
}

/// <summary>
/// contracts/voice-stt-session.md — the answer to "how should this turn be dictated?". The server
/// decides, so the browser never has to discover an administrator's choice by being refused.
/// </summary>
public sealed record DictationSession(DictationEngine Engine, string? Token, DateTime? ExpiresAtUtc)
{
    public static DictationSession Realtime(string token, DateTime expiresAtUtc) =>
        new(DictationEngine.Realtime, token, expiresAtUtc);

    public static DictationSession Whisper { get; } = new(DictationEngine.Whisper, null, null);
}
