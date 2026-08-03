using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai;

/// <summary>
/// One event in the multiplexed `/api/v1/ai/voice/reply` stream (contracts/
/// voice-reply-stream.md) — a single record with a `Type` discriminator, matching the
/// existing convention of <see cref="StreamChunk"/> rather than a class hierarchy (constitution
/// §III Simplicity First). The controller serializes each event's non-null fields into the
/// `data: {...}\n\n` JSON envelope the contract documents.
/// </summary>
public sealed record VoiceReplyEvent(
    string Type,
    string? TranscriptDelta = null,
    int? AudioSequence = null,
    byte[]? AudioBytes = null,
    string? VoiceProvider = null,
    ChatUsage? Usage = null)
{
    public static VoiceReplyEvent TranscriptDeltaEvent(string content) => new("transcript-delta", TranscriptDelta: content);

    public static VoiceReplyEvent AudioChunkEvent(int sequence, byte[] audio) => new("audio-chunk", AudioSequence: sequence, AudioBytes: audio);

    public static VoiceReplyEvent ProviderStatusEvent(string voiceProvider) => new("provider-status", VoiceProvider: voiceProvider);

    public static VoiceReplyEvent AudioFailedEvent() => new("audio-failed");

    public static VoiceReplyEvent UsageEvent(ChatUsage usage) => new("usage", Usage: usage);

    public static VoiceReplyEvent DoneEvent() => new("done");
}
