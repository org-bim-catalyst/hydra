namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>
/// Bound from configuration (constitution §8). Every value has a code default, so a missing
/// <c>LocalWhisper</c> section never fails the host (specs/078 research D1). There is no model
/// file setting: the model is the Custom Models deployment an admin selects.
/// </summary>
public sealed class LocalWhisperOptions
{
    public const string SectionName = "LocalWhisper";

    /// <summary>How many clips Local Whisper transcribes at once. whisper.cpp is CPU-bound,
    /// so more parallel clips only slow every clip down on a shared host.</summary>
    public int MaxConcurrentTranscriptions { get; init; } = 2;

    /// <summary>How long a clip waits for a free slot before it counts as a Local Whisper
    /// failure (and the user is moved to the browser built-in).</summary>
    public int QueueTimeoutSeconds { get; init; } = 10;
}
