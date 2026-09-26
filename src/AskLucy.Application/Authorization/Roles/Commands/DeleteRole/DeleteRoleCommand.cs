using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.DeleteRole;

/// <summary>Deletes a custom role, moving every current holder to the built-in User role (FR-007) - never to no role. Returns how many users were moved.</summary>
public sealed record DeleteRoleCommand(string RoleId, string ConcurrencyStamp) : IRequest<DeleteRoleResult>;

public sealed record DeleteRoleResult(int ReassignedUserCount);
