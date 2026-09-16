using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.CreateRole;

/// <summary>Creates a custom role (spec.md FR-003/FR-004) — Administrator/Super User only, enforced by the controller's reserved <c>AdministratorOrSuperUser</c> policy (FR-002).</summary>
public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> PermissionKeys) : IRequest<RoleSummaryDto>;
