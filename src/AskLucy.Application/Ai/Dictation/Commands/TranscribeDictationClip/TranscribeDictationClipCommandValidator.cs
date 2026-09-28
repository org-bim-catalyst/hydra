using FluentValidation;

namespace AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;

public sealed class TranscribeDictationClipCommandValidator : AbstractValidator<TranscribeDictationClipCommand>
{
    public TranscribeDictationClipCommandValidator()
    {
        RuleFor(c => c.Wav).NotNull();
        RuleFor(c => c.Language)
            .Matches("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$").WithMessage("Choose a supported language.")
            .When(c => !string.IsNullOrEmpty(c.Language));
    }
}
