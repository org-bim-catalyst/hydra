using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;

namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>specs/078 FR-009c — "Try it", over the shared <see cref="LocalWhisperRuntime"/>.</summary>
public sealed class LocalWhisperModelTrial(LocalWhisperRuntime runtime) : ILocalWhisperModelTrial
{
    public Task<DictationTranscript> TryAsync(string modelPath, Stream wav, string? language, CancellationToken cancellationToken = default) =>
        runtime.TryAsync(modelPath, wav, language, cancellationToken);
}
