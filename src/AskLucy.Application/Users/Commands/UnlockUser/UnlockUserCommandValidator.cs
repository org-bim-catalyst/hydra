using FluentValidation;

namespace AskLucy.Application.Users.Commands.UnlockUser;

public sealed class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
