using FluentValidation;

namespace AskLucy.Application.Authorization.Assignments.Commands.AssignRole;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
    }
}
