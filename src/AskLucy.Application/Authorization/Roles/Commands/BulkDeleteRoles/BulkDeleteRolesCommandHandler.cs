using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;

public sealed class BulkDeleteRolesCommandHandler(
    ISender mediator,
    IRoleRepository roleRepository,
    ICurrentUserAccessor currentUser,
    IAuthorizationCacheInvalidator cacheInvalidator) : IRequestHandler<BulkDeleteRolesCommand, BulkDeleteRolesResult>
{
    public async Task<BulkDeleteRolesResult> Handle(BulkDeleteRolesCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetRolesEligibleIdsQuery(request.Search), cancellationToken)
            : request.Target.Ids!;

        // All-or-nothing (FR-016j): checked before the first delete, not per row.
        await SuperUserControlledPermissionGuard.EnsureCanDeleteAllAsync(currentUser, roleRepository, ids, cancellationToken);

        var succeeded = 0;
        var skipped = new List<BulkActionSkip>();
        var unassignedUserCounts = new Dictionary<string, int>();

        foreach (var id in ids)
        {
            var affectedUserIds = await roleRepository.DeleteByIdAsync(id, actorUserId, cancellationToken);
            if (affectedUserIds is null)
            {
                skipped.Add(new BulkActionSkip(id, "NotFound"));
                continue;
            }

            foreach (var userId in affectedUserIds)
            {
                cacheInvalidator.Evict(userId);
            }

            unassignedUserCounts[id] = affectedUserIds.Count;
            succeeded++;
        }

        return new BulkDeleteRolesResult(new BulkActionOutcome(succeeded, skipped), unassignedUserCounts);
    }
}
