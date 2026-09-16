using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;

public sealed class GetRolesEligibleIdsQueryHandler(IRoleRepository roleRepository)
    : IRequestHandler<GetRolesEligibleIdsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetRolesEligibleIdsQuery request, CancellationToken cancellationToken) =>
        roleRepository.ListEligibleIdsAsync(request.Search, cancellationToken);
}
