using FluentValidation;

namespace AskLucy.Application.Users.Commands.LockUser;

public sealed class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
