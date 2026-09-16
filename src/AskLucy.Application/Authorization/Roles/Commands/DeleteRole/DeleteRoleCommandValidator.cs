using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.DeleteRole;

public sealed class DeleteRoleCommandValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleCommandValidator()
    {
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.ConcurrencyStamp).NotEmpty();
    }
}
