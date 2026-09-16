using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;

public sealed record GetRoleAssignmentsEligibleIdsQuery(string RoleId, string? Search) : IRequest<IReadOnlyList<string>>;
