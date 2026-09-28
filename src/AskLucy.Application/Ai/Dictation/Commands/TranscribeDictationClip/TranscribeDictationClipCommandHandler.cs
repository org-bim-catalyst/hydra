using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;

/// <summary>
/// contracts/dictation-transcription.md — transcribes one recorded dictation clip on the engine
/// <see cref="DictationEngineSetting.ResolveEngine"/> resolves for this turn. This story
/// (specs/078 US1) implements steps 1–4: resolving the engine, validating the WAV header, picking
/// the one transcriber for that engine, and reporting success. A transcriber failure (step 5,
/// including Local Whisper's never-suspend rule) arrives with US2.
/// </summary>
public sealed class TranscribeDictationClipCommandHandler(
    IDictationEngineSettingRepository settings,
    ILocalWhisperModelCatalog catalog,
    IEnumerable<IDictationClipTranscriber> transcribers,
    IVoiceFailureReporter failureReporter) : IRequestHandler<TranscribeDictationClipCommand, DictationTranscriptionResult>
{
    private readonly record struct ResolvedClipEngine(DictationClipEngine Engine, string? ModelPath, string? ModelLabel);

    public async Task<DictationTranscriptionResult> Handle(TranscribeDictationClipCommand request, CancellationToken cancellationToken)
    {
        var setting = await settings.GetOrCreateAsync(cancellationToken);
        var turnEngine = setting.ResolveEngine(request.Mode);

        var resolved = await ResolveClipEngineAsync(turnEngine, setting.LocalWhisperModelId, cancellationToken);
        if (resolved is not { } clipEngine)
        {
            // The browser built-in, no model selected, an Unavailable model, or (later, US3) a
            // Suspended engine: the client shouldn't have recorded, so nothing is reported.
            return DictationTranscriptionResult.Unavailable.Instance;
        }

        if (!WavHeader.TryRead(request.Wav, out _, out var wavError))
        {
            throw new DictationAudioInvalidException(wavError ?? "The clip isn't a 16 kHz mono WAV recording.");
        }

        var transcriber = transcribers.Single(t => t.Engine == clipEngine.Engine);
        var clip = new DictationClip(request.Wav, request.Language, clipEngine.ModelPath);
        var transcript = await transcriber.TranscribeAsync(clip, cancellationToken);

        var identity = clipEngine.Engine == DictationClipEngine.LocalWhisper
            ? new VoiceEngineIdentity("Local Whisper", null, clipEngine.ModelLabel)
            : new VoiceEngineIdentity("OpenAI Whisper");
        failureReporter.ReportServed(VoiceOperations.Transcription, identity);

        return new DictationTranscriptionResult.Transcribed(transcript.Text, transcript.DetectedLanguage ?? request.Language);
    }

    private async Task<ResolvedClipEngine?> ResolveClipEngineAsync(
        DictationTurnEngine turnEngine, Guid? localWhisperModelId, CancellationToken cancellationToken)
    {
        switch (turnEngine)
        {
            case DictationTurnEngine.OpenAiWhisper:
                return new ResolvedClipEngine(DictationClipEngine.OpenAiWhisper, null, null);

            case DictationTurnEngine.LocalWhisper:
                var resolution = await catalog.ResolveSelectedAsync(localWhisperModelId, cancellationToken);
                return resolution is LocalWhisperModelResolution.Ready ready
                    ? new ResolvedClipEngine(DictationClipEngine.LocalWhisper, ready.ModelPath, ready.FileName)
                    : null;

            default:
                return null;
        }
    }
}
