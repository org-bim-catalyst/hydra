using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;

public sealed class GetRoleAssignmentsEligibleIdsQueryHandler(
    IRoleAssignmentRepository roleAssignmentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetRoleAssignmentsEligibleIdsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetRoleAssignmentsEligibleIdsQuery request, CancellationToken cancellationToken)
    {
        var actorIsSuperUser = currentUser.IsInRole(PrivilegedRoleNames.SuperUser);
        return roleAssignmentRepository.ListEligibleIdsAsync(request.RoleId, request.Search, actorIsSuperUser, cancellationToken);
    }
}
