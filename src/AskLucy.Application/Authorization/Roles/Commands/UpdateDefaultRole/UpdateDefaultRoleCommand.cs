using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateDefaultRole;

/// <summary>
/// Edits the built-in User role (<see cref="DefaultRole"/>): its description and the permissions
/// added on top of its baseline. The name is fixed, and the baseline can't be removed - the role
/// must always carry what any account needs to use the site.
/// </summary>
public sealed record UpdateDefaultRoleCommand(
    string? Description, IReadOnlyList<string> PermissionKeys, string ConcurrencyStamp) : IRequest<RoleSummaryDto>;
