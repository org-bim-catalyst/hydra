using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetAdministratorContentAccess;

public sealed class GetAdministratorContentAccessQueryHandler(IRoleRepository roleRepository)
    : IRequestHandler<GetAdministratorContentAccessQuery, AdministratorContentAccessDto>
{
    public async Task<AdministratorContentAccessDto> Handle(GetAdministratorContentAccessQuery request, CancellationToken cancellationToken)
    {
        var administrator = await roleRepository.GetByNormalizedNameAsync(PrivilegedRoleNames.Administrator.ToUpperInvariant(), cancellationToken)
            ?? throw new KeyNotFoundException("The built-in Administrator role was not found.");

        return new AdministratorContentAccessDto(administrator.Permissions.Contains(AdminPermissionCatalog.OperationalFailuresContentView));
    }
}
