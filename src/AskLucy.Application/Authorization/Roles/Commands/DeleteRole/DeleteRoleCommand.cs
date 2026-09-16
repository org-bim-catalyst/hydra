using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.DeleteRole;

/// <summary>Deletes a custom role, leaving every current holder with no role (FR-007). Returns how many users were affected.</summary>
public sealed record DeleteRoleCommand(string RoleId, string ConcurrencyStamp) : IRequest<DeleteRoleResult>;

public sealed record DeleteRoleResult(int UnassignedUserCount);
