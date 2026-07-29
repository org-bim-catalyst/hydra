using FluentValidation;

namespace AskLucy.Application.Users.Commands.DeleteUser;

public sealed class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
