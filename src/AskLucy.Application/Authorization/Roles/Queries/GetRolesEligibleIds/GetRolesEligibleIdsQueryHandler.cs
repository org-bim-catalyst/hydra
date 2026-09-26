using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;

public sealed class GetRolesEligibleIdsQueryHandler(IRoleRepository roleRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetRolesEligibleIdsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetRolesEligibleIdsQuery request, CancellationToken cancellationToken) =>
        roleRepository.ListEligibleIdsAsync(request.Search, currentUser.IsInRole(PrivilegedRoleNames.SuperUser), cancellationToken);
}
