using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.RemoveExternalLogin;

public sealed class RemoveExternalLoginCommandValidator : AbstractValidator<RemoveExternalLoginCommand>
{
    public RemoveExternalLoginCommandValidator()
    {
        RuleFor(c => c.Provider).NotEmpty();
        RuleFor(c => c.ProviderKey).NotEmpty();
    }
}
