using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetRole;

public sealed record GetRoleQuery(string RoleId) : IRequest<RoleSummaryDto?>;
