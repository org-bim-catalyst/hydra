using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Queries.ListRoles;

public sealed class ListRolesQueryHandler(IRoleRepository roleRepository) : IRequestHandler<ListRolesQuery, PagedResult<RoleSummaryDto>>
{
    public async Task<PagedResult<RoleSummaryDto>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await roleRepository.SearchAsync(request.Search, request.Page, request.PageSize, cancellationToken);
        return new PagedResult<RoleSummaryDto>([.. items.Select(RoleSummaryDto.Create)], totalCount, request.Page, request.PageSize);
    }
}
