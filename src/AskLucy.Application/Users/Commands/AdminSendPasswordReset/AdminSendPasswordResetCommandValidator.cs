using FluentValidation;

namespace AskLucy.Application.Users.Commands.AdminSendPasswordReset;

public sealed class AdminSendPasswordResetCommandValidator : AbstractValidator<AdminSendPasswordResetCommand>
{
    public AdminSendPasswordResetCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
