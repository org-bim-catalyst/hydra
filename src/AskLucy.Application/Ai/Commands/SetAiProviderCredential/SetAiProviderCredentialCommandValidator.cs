using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SetAiProviderCredential;

public sealed class SetAiProviderCredentialCommandValidator : AbstractValidator<SetAiProviderCredentialCommand>
{
    public SetAiProviderCredentialCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.ApiKey).NotEmpty().WithMessage("An API key is required.");
    }
}
