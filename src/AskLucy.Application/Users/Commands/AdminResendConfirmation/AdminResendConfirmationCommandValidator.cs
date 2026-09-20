using FluentValidation;

namespace AskLucy.Application.Users.Commands.AdminResendConfirmation;

public sealed class AdminResendConfirmationCommandValidator : AbstractValidator<AdminResendConfirmationCommand>
{
    public AdminResendConfirmationCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
