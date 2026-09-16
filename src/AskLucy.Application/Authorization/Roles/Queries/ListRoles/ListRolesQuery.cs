using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.ListRoles;

public sealed record ListRolesQuery(string? Search, int Page = 1, int PageSize = 20) : IRequest<PagedResult<RoleSummaryDto>>;
