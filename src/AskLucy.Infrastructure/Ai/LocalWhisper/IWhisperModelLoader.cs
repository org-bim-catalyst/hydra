using System.Runtime.CompilerServices;
using Whisper.net;

namespace AskLucy.Infrastructure.Ai.LocalWhisper;

/// <summary>specs/078 — one loaded Local Whisper model. Disposing it frees its native memory.</summary>
public interface IWhisperModel : IDisposable
{
    /// <summary>The transcript segments of one 16 kHz mono PCM WAV clip.</summary>
    IAsyncEnumerable<string> TranscribeAsync(Stream wav, string? language, CancellationToken cancellationToken);
}

/// <summary>specs/078 — loads a ggml model file; the seam that keeps the transcriber testable without a real model.</summary>
public interface IWhisperModelLoader
{
    IWhisperModel Load(string modelPath);
}

/// <summary>Whisper.net (whisper.cpp) in-process.</summary>
public sealed class WhisperNetModelLoader : IWhisperModelLoader
{
    public IWhisperModel Load(string modelPath) => new WhisperNetModel(WhisperFactory.FromPath(modelPath));

    private sealed class WhisperNetModel(WhisperFactory factory) : IWhisperModel
    {
        public async IAsyncEnumerable<string> TranscribeAsync(
            Stream wav, string? language, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var builder = factory.CreateBuilder();
            builder = string.IsNullOrWhiteSpace(language) ? builder.WithLanguageDetection() : builder.WithLanguage(language);

            await using var processor = builder.Build();
            await foreach (var segment in processor.ProcessAsync(wav, cancellationToken))
            {
                yield return segment.Text;
            }
        }

        public void Dispose() => factory.Dispose();
    }
}
