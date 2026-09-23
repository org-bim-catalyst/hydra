using AskLucy.Domain.Ai;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.PreviewVoice;

public sealed class PreviewVoiceCommandValidator : AbstractValidator<PreviewVoiceCommand>
{
    public const int MaxTextLength = 500;

    public PreviewVoiceCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.VoiceId).NotEmpty().WithMessage("Choose a voice.").MaximumLength(VoiceProvider.MaxVoiceIdLength);
        RuleFor(c => c.Text)
            .NotEmpty().WithMessage("Enter a sample sentence.")
            .MaximumLength(MaxTextLength)
            .Must(ContainsSpeakableText).WithMessage("The sample sentence needs at least one letter or digit.");
        RuleFor(c => c.Language)
            .NotEmpty()
            .Matches("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$").WithMessage("Choose a supported language.");
    }

    private static bool ContainsSpeakableText(string? text) => text is not null && text.Any(char.IsLetterOrDigit);
}
