using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;

public sealed class BulkDeleteRolesCommandValidator : AbstractValidator<BulkDeleteRolesCommand>
{
    public BulkDeleteRolesCommandValidator()
    {
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
