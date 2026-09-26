using AskLucy.Domain.Authorization;
using FluentValidation;

namespace AskLucy.Application.Authorization.Roles.Commands.DuplicateRole;

/// <summary>The same name rules as <c>CreateRoleCommandValidator</c> - a duplicate is a new custom role.</summary>
public sealed class DuplicateRoleCommandValidator : AbstractValidator<DuplicateRoleCommand>
{
    public DuplicateRoleCommandValidator()
    {
        RuleFor(c => c.SourceRoleId).NotEmpty();
        RuleFor(c => c.Name)
            .NotEmpty()
            .Must(name => name.Trim().Length is >= 2 and <= 50)
            .WithMessage("Role name must be 2 to 50 characters.")
            .Must(name => !RoleName.IsNameReserved(name))
            .WithMessage("That name is reserved and cannot be used for a custom role.");
        RuleFor(c => c.Description).MaximumLength(250);
    }
}
