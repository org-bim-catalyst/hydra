using FluentValidation;

namespace AskLucy.Application.Ai.Commands.Transcribe;

public sealed class TranscribeAudioCommandValidator : AbstractValidator<TranscribeAudioCommand>
{
    public TranscribeAudioCommandValidator()
    {
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.ContentType).NotEmpty();
    }
}
