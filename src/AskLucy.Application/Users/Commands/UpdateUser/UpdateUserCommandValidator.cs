using FluentValidation;

namespace AskLucy.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
