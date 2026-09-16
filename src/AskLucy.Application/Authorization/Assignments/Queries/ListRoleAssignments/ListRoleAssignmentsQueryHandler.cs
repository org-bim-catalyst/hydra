using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Queries.ListRoleAssignments;

public sealed class ListRoleAssignmentsQueryHandler(IRoleAssignmentRepository assignmentRepository)
    : IRequestHandler<ListRoleAssignmentsQuery, PagedResult<RoleAssignmentDto>>
{
    private const string NoRoleSentinel = "none";

    public async Task<PagedResult<RoleAssignmentDto>> Handle(ListRoleAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var noRoleFilter = request.RoleId == NoRoleSentinel;
        var roleIdFilter = noRoleFilter ? null : request.RoleId;

        var (items, totalCount) = await assignmentRepository.SearchAsync(
            request.Search, roleIdFilter, noRoleFilter, request.AssignableOnly, request.Page, request.PageSize, cancellationToken);

        return new PagedResult<RoleAssignmentDto>([.. items.Select(RoleAssignmentDto.Create)], totalCount, request.Page, request.PageSize);
    }
}
