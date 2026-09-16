using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Queries.ListRoleAssignments;

/// <summary>contracts/admin-roles-api.md §3. <paramref name="RoleId"/> = <c>"none"</c> means "users with no role" (FR-013).</summary>
public sealed record ListRoleAssignmentsQuery(
    string? Search, string? RoleId, bool AssignableOnly, int Page = 1, int PageSize = 20) : IRequest<PagedResult<RoleAssignmentDto>>;
