using AskLucy.Domain.Ai;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;

public sealed class SetPrimaryVoiceProviderCommandValidator : AbstractValidator<SetPrimaryVoiceProviderCommand>
{
    public SetPrimaryVoiceProviderCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.VoiceId).NotEmpty().WithMessage("Choose a voice.").MaximumLength(VoiceProvider.MaxVoiceIdLength);
    }
}
