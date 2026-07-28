using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Token).NotEmpty();
    }
}
