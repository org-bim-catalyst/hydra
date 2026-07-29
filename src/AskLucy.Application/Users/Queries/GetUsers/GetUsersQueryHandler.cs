using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Queries.GetUsers;

/// <summary>Admin-only listing (FR-017, enforced at the endpoint), never the raw Identity entity (FR-019).</summary>
public sealed class GetUsersQueryHandler(IUserAdminRepository userAdminRepository)
    : IRequestHandler<GetUsersQuery, PagedResult<UserAdminDto>>
{
    public Task<PagedResult<UserAdminDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken) =>
        userAdminRepository.SearchAsync(
            request.Search, request.SortBy, request.SortDescending, request.Page, request.PageSize, cancellationToken);
}
