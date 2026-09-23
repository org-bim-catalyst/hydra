using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;

public sealed class SetVoiceProviderCredentialCommandValidator : AbstractValidator<SetVoiceProviderCredentialCommand>
{
    public SetVoiceProviderCredentialCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.ApiKey).NotEmpty().WithMessage("An API key is required.").MaximumLength(500);
    }
}
