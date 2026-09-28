using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;

/// <summary>contracts/dictation-transcription.md `POST /api/v1/ai/voice/transcriptions`.</summary>
public sealed record TranscribeDictationClipCommand(
    Stream Wav,
    string? Language,
    DictationCaptureMode Mode = DictationCaptureMode.Continuous) : IRequest<DictationTranscriptionResult>;

/// <summary>What transcribing one dictation clip produced.</summary>
public abstract record DictationTranscriptionResult
{
    private DictationTranscriptionResult()
    {
    }

    /// <param name="Language">The engine's detected language, else the caller's hint, else null.</param>
    public sealed record Transcribed(string Text, string? Language) : DictationTranscriptionResult;

    /// <summary>
    /// Nothing was configured to serve this clip (the browser built-in, no model selected, an
    /// Unavailable model, or a Suspended engine) — the client shouldn't have recorded and posted
    /// in the first place, so nothing is reported (contracts/dictation-transcription.md step 1).
    /// </summary>
    public sealed record Unavailable : DictationTranscriptionResult
    {
        public static readonly Unavailable Instance = new();
    }
}
