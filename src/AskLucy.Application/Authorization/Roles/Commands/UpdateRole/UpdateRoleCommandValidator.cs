using AskLucy.Domain.Authorization;
using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateRole;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.ConcurrencyStamp).NotEmpty();

        RuleFor(c => c.Name)
            .NotEmpty()
            .Must(name => name.Trim().Length is >= 2 and <= 50)
            .WithMessage("Role name must be between 2 and 50 characters.")
            .Must(name => !RoleName.IsNameReserved(name))
            .WithMessage("That name is reserved and cannot be used for a custom role.");

        RuleFor(c => c.Description).MaximumLength(250);

        RuleFor(c => c.PermissionKeys)
            .NotEmpty().WithMessage("A role must include at least one permission.");

        RuleForEach(c => c.PermissionKeys)
            .Must(key => AdminPermissionCatalog.TryGet(key, out _))
            .WithMessage("'{PropertyValue}' is not a recognized permission.");
    }
}
