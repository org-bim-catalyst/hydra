using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Queries.GetUsersEligibleIds;

public sealed class GetUsersEligibleIdsQueryHandler(
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUsersEligibleIdsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetUsersEligibleIdsQuery request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        return await userAdminRepository.ListEligibleIdsAsync(request.Search, request.Action, actorUserId, cancellationToken);
    }
}
