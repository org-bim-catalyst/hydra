using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;

public sealed class BulkAssignRoleCommandValidator : AbstractValidator<BulkAssignRoleCommand>
{
    public BulkAssignRoleCommandValidator()
    {
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
