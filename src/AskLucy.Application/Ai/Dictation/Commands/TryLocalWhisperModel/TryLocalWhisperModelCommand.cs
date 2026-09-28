using AskLucy.Application.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation.Commands.TryLocalWhisperModel;

/// <summary>
/// specs/078 FR-009c — transcribes one sample on a deployment that may not be selected, so an
/// administrator can judge a model before choosing it. Never changes the setting; one try at a time.
/// </summary>
public sealed record TryLocalWhisperModelCommand(Guid CustomModelId, Stream Wav, string? Language) : IRequest<LocalWhisperTryResultDto>;

public sealed record LocalWhisperTryResultDto(string Text, long ElapsedMs, string ModelLabel);

public sealed class TryLocalWhisperModelCommandValidator : AbstractValidator<TryLocalWhisperModelCommand>
{
    public TryLocalWhisperModelCommandValidator()
    {
        RuleFor(c => c.CustomModelId).NotEmpty();
        RuleFor(c => c.Wav).NotNull();
        RuleFor(c => c.Language).Matches("^[a-z]{2}(-[A-Za-z]{2})?$").When(c => !string.IsNullOrEmpty(c.Language));
    }
}

public sealed class TryLocalWhisperModelCommandHandler(
    ILocalWhisperModelCatalog catalog,
    ILocalWhisperModelTrial trial,
    ICurrentUserAccessor currentUser,
    ILogger<TryLocalWhisperModelCommandHandler> logger) : IRequestHandler<TryLocalWhisperModelCommand, LocalWhisperTryResultDto>
{
    public async Task<LocalWhisperTryResultDto> Handle(TryLocalWhisperModelCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (modelPath, label) = await catalog.ResolveForTrialAsync(request.CustomModelId, cancellationToken) switch
        {
            LocalWhisperModelResolution.Ready ready => (ready.ModelPath, ready.ModelLabel),
            LocalWhisperModelResolution.Broken broken => throw new LocalWhisperModelNotSelectableException(broken.Reason),
            LocalWhisperModelResolution.Unavailable unavailable => throw new LocalWhisperModelNotSelectableException(unavailable.Reason),
            _ => throw new LocalWhisperModelNotSelectableException("Choose a model to try."),
        };

        if (!WavHeader.TryRead(request.Wav, out _, out var wavError))
        {
            throw new DictationAudioInvalidException(wavError ?? "The sample isn't a 16 kHz mono WAV recording.");
        }

        DictationTranscript transcript;
        try
        {
            transcript = await trial.TryAsync(modelPath, request.Wav, request.Language, cancellationToken);
        }
        catch (Exception ex) when (ex is not (LocalWhisperTrialBusyException or OperationCanceledException))
        {
            // FR-009c: an administrator's try affects no user, so it is logged, not put on the trail.
            DictationAdminLog.TrialFailed(logger, ex, request.CustomModelId, actorUserId);
            throw new LocalWhisperTrialFailedException(
                ex is AiProviderException ? ex.Message : "The model couldn't transcribe the sample.", ex);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = request.CustomModelId.ToString();
            DictationAdminLog.ActionPerformed(logger, "TryLocalWhisperModel", actorUserId, detail);
        }

        return new LocalWhisperTryResultDto(transcript.Text, (long)transcript.Elapsed.TotalMilliseconds, label);
    }
}
