using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.ChangePassword;

/// <summary>
/// Shape only. "Is the current password required?" depends on whether the account has one, which
/// only the handler can answer, and the policy itself stays in ASP.NET Identity so the two cannot
/// drift (contracts/password-api.md).
/// </summary>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty();
    }
}
