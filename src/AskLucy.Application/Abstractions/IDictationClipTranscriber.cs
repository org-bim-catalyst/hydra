using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/078 research D4 — transcribes one recorded dictation clip (16 kHz mono PCM WAV) on one
/// engine. There is one implementation per <see cref="DictationClipEngine"/> that records clips
/// (Local Whisper, OpenAI Whisper). A caller picks exactly one and never tries another: a failure
/// falls to the browser built-in only (FR-005).
/// </summary>
public interface IDictationClipTranscriber
{
    DictationClipEngine Engine { get; }

    /// <summary>Throws on failure; the caller classifies and reports the exception.</summary>
    Task<DictationTranscript> TranscribeAsync(DictationClip clip, CancellationToken cancellationToken = default);
}
