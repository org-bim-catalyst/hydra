using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.LoginTwoFactor;

public sealed class LoginTwoFactorCommandValidator : AbstractValidator<LoginTwoFactorCommand>
{
    public LoginTwoFactorCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Code).NotEmpty();
    }
}
