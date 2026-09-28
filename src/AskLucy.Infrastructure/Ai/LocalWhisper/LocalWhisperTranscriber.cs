using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;

namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>specs/078 research D4 — the Local Whisper clip engine, over the shared <see cref="LocalWhisperRuntime"/>.</summary>
public sealed class LocalWhisperTranscriber(LocalWhisperRuntime runtime) : IDictationClipTranscriber
{
    public DictationClipEngine Engine => DictationClipEngine.LocalWhisper;

    public Task<DictationTranscript> TranscribeAsync(DictationClip clip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var modelPath = clip.LocalWhisperModelPath
            ?? throw new InvalidOperationException("Local Whisper needs the resolved model file.");
        return runtime.TranscribeAsync(modelPath, clip.Wav, clip.Language, cancellationToken);
    }
}
