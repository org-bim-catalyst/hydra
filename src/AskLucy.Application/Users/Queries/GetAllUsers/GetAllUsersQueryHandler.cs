using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Queries.GetAllUsers;

/// <summary>Admin-only listing (FR-017, enforced at the endpoint), never the raw Identity entity (FR-019).</summary>
public sealed class GetAllUsersQueryHandler(IUserAdminRepository userAdminRepository)
    : IRequestHandler<GetAllUsersQuery, IReadOnlyList<UserAdminDto>>
{
    public Task<IReadOnlyList<UserAdminDto>> Handle(GetAllUsersQuery request, CancellationToken cancellationToken) =>
        userAdminRepository.ListAllAsync(cancellationToken);
}
