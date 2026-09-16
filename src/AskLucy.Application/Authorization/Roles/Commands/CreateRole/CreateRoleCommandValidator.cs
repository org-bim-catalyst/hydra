using AskLucy.Domain.Authorization;
using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.CreateRole;

/// <summary>Mirrors <see cref="RoleName"/>/<see cref="PermissionSet"/>'s own rules so a bad request is a 400 with a field-level message, not a 500 from an unhandled <see cref="ArgumentException"/> deep in the handler.</summary>
public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
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
