using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateRole;

/// <summary>Updates a custom role's name/description/permissions (FR-005/FR-006). Built-in roles reject this (FR-008) — enforced by the handler, since the reserved policy alone doesn't distinguish built-in from custom.</summary>
public sealed record UpdateRoleCommand(
    string RoleId, string Name, string? Description, IReadOnlyList<string> PermissionKeys, string ConcurrencyStamp) : IRequest<RoleSummaryDto>;
