using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Authorization.Assignments.Queries.ListRoleAssignments;

public sealed record RoleAssignmentRoleDto(string Id, string Name, bool IsBuiltIn);

/// <summary>contracts/admin-roles-api.md §3 — <see cref="Role"/> is <see langword="null"/> for a user with no role.</summary>
public sealed record RoleAssignmentDto(
    string UserId, string Email, string? FirstName, string? LastName, bool IsLockedOut, RoleAssignmentRoleDto? Role)
{
    public static RoleAssignmentDto Create(RoleAssignmentRecord record) => new(
        record.UserId, record.Email, record.FirstName, record.LastName, record.IsLockedOut,
        record.RoleId is null ? null : new RoleAssignmentRoleDto(record.RoleId, record.RoleName!, record.RoleIsBuiltIn));
}
