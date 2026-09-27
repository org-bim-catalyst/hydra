using FluentValidation;

namespace AskLucy.Application.Ai.Commands.Transcribe;

public sealed class TranscribeAudioCommandValidator : AbstractValidator<TranscribeAudioCommand>
{
    public TranscribeAudioCommandValidator()
    {
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.ContentType).NotEmpty();
        RuleFor(c => c.Language)
            .Matches("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$").WithMessage("Choose a supported language.")
            .When(c => c.Language is not null);
    }
}
