using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// specs/078 research D4 — the OpenAI Whisper clip engine, adapting the same OpenAI transcription
/// call (<c>whisper-1</c>) the file-attach upload uses
/// (<see cref="AskLucy.Application.Ai.Commands.Transcribe.TranscribeAudioCommandHandler"/>). The
/// unkeyed <see cref="IAIProvider"/> is wired to OpenAI directly (Infrastructure
/// DependencyInjection), so this already prefers the DB credential the same way every other OpenAI
/// call does — nothing extra to resolve here. Provider exceptions pass through unchanged for the
/// caller's own classification (<c>DictationFailurePolicy</c>).
/// </summary>
public sealed class OpenAiWhisperClipTranscriber(IAIProvider openAi) : IDictationClipTranscriber
{
    public DictationClipEngine Engine => DictationClipEngine.OpenAiWhisper;

    public async Task<DictationTranscript> TranscribeAsync(DictationClip clip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var stopwatch = Stopwatch.StartNew();
        var text = await openAi.TranscribeAudioAsync(clip.Wav, "clip.wav", "audio/wav", clip.Language, cancellationToken);
        return new DictationTranscript(text, clip.Language, stopwatch.Elapsed);
    }
}
