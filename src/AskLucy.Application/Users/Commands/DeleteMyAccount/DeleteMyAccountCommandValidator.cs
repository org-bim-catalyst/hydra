using FluentValidation;

namespace AskLucy.Application.Users.Commands.DeleteMyAccount;

public sealed class DeleteMyAccountCommandValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountCommandValidator()
    {
        RuleFor(c => c.Password).NotEmpty();
    }
}
