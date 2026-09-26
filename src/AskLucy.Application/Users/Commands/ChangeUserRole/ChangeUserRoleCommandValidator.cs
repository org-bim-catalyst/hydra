using AskLucy.Application.Authorization;
using AskLucy.Application.Users;
using FluentValidation;

namespace AskLucy.Application.Users.Commands.ChangeUserRole;

public sealed class ChangeUserRoleCommandValidator : AbstractValidator<ChangeUserRoleCommand>
{
    private static readonly string[] AllowedRoles = [.. PrivilegedRoleNames.All, DefaultRole.Name, PrivilegedRoleNames.Regular];

    public ChangeUserRoleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.NewRole).NotEmpty().Must(role => AllowedRoles.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}.");
    }
}
