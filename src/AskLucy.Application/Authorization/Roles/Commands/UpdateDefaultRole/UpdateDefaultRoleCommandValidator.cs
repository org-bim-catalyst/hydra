using AskLucy.Domain.Authorization;
using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateDefaultRole;

/// <summary>Unlike a custom role, the User role may hold no permissions beyond its baseline.</summary>
public sealed class UpdateDefaultRoleCommandValidator : AbstractValidator<UpdateDefaultRoleCommand>
{
    public UpdateDefaultRoleCommandValidator()
    {
        RuleFor(c => c.ConcurrencyStamp).NotEmpty();
        RuleFor(c => c.Description).MaximumLength(250);
        RuleFor(c => c.PermissionKeys).NotNull();
        RuleForEach(c => c.PermissionKeys)
            .Must(key => AdminPermissionCatalog.TryGet(key, out _))
            .WithMessage("'{PropertyValue}' is not a recognized permission.");
    }
}
