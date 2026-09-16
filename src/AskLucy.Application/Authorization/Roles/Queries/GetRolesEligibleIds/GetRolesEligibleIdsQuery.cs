using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;

public sealed record GetRolesEligibleIdsQuery(string? Search) : IRequest<IReadOnlyList<string>>;
