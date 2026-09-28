using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;

/// <summary>
/// contracts/dictation-transcription.md — transcribes one recorded dictation clip on the engine
/// <see cref="DictationEngineSetting.ResolveEngine"/> resolves for this turn: resolving the engine,
/// validating the WAV header, picking the one transcriber for that engine, and reporting success or
/// failure. specs/078 US2 — a transcriber failure is a failover (no second transcriber to try, so
/// the client's own next attempt is the browser built-in); Local Whisper never suspends (FR-005).
/// An invalid WAV clip is a validation failure, not a failover — nothing served it.
/// </summary>
public sealed class TranscribeDictationClipCommandHandler(
    IDictationEngineSettingRepository settings,
    ILocalWhisperModelCatalog catalog,
    IEnumerable<IDictationClipTranscriber> transcribers,
    IVoiceFailureReporter failureReporter,
    DictationFailurePolicy failurePolicy) : IRequestHandler<TranscribeDictationClipCommand, DictationTranscriptionResult>
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

        var identity = clipEngine.Engine == DictationClipEngine.LocalWhisper
            ? new VoiceEngineIdentity("Local Whisper", null, clipEngine.ModelLabel)
            : new VoiceEngineIdentity("OpenAI Whisper");

        if (!WavHeader.TryRead(request.Wav, out _, out var wavError))
        {
            var invalid = new DictationAudioInvalidException(wavError ?? "The clip isn't a 16 kHz mono WAV recording.");
            failurePolicy.ReportValidationFailure(VoiceOperations.Transcription, identity, invalid, cancellationToken);
            throw invalid;
        }

        var transcriber = transcribers.Single(t => t.Engine == clipEngine.Engine);
        var clip = new DictationClip(request.Wav, request.Language, clipEngine.ModelPath);

        DictationTranscript transcript;
        try
        {
            transcript = await transcriber.TranscribeAsync(clip, cancellationToken);
        }
        catch (Exception ex)
        {
            // No second transcriber to try for this turn (research D4): the client's own next
            // attempt is the browser built-in, so there is nothing left to serve this one. Whether
            // to suspend a cloud engine's future turns is US3 (T066); Local Whisper never suspends.
            var isCloudEngine = clipEngine.Engine == DictationClipEngine.OpenAiWhisper;
            failurePolicy.ReportEngineFailure(VoiceOperations.Transcription, identity, isCloudEngine, ex, cancellationToken);
            return DictationTranscriptionResult.Unavailable.Instance;
        }

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
