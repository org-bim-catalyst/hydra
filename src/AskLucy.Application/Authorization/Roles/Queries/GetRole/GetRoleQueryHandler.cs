using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetRole;

public sealed class GetRoleQueryHandler(IRoleRepository roleRepository) : IRequestHandler<GetRoleQuery, RoleSummaryDto?>
{
    public async Task<RoleSummaryDto?> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        return role is null ? null : RoleSummaryDto.Create(role);
    }
}
