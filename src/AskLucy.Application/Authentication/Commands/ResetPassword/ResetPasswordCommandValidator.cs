using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.ResetPassword;

/// <summary>
/// Shape only. The password policy itself lives in ASP.NET Identity and is applied there, so the
/// two can never drift apart (contracts/password-api.md); this validator deliberately does not
/// restate any rule beyond "not empty".
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Token).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).NotEmpty();
    }
}
