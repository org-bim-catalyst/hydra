using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;
using AskLucy.Application.Common;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;

public sealed class BulkAssignRoleCommandHandler(
    ISender mediator,
    IRoleRepository roleRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<BulkAssignRoleCommand, BulkActionOutcome>
{
    public async Task<BulkActionOutcome> Handle(BulkAssignRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetRoleAssignmentsEligibleIdsQuery(request.RoleId, request.Search), cancellationToken)
            : request.Target.Ids!;

        await SuperUserControlledPermissionGuard.EnsureCanBulkAssignAsync(
            currentUser, roleRepository, roleAssignmentRepository, request.RoleId, ids, cancellationToken);

        var result = await roleAssignmentRepository.BulkReplaceRoleAsync(request.RoleId, ids, actorUserId, cancellationToken);

        var skipped = result.Skipped.Select(s => new BulkActionSkip(s.UserId, s.Reason.ToString())).ToList();
        return new BulkActionOutcome(result.AssignedCount, skipped);
    }
}
