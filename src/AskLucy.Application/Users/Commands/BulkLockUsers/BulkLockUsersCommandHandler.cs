using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.BulkLockUsers;

public sealed class BulkLockUsersCommandHandler(
    ISender mediator,
    IIdentityService identityService,
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser,
    ILogger<BulkLockUsersCommandHandler> logger) : IRequestHandler<BulkLockUsersCommand, BulkActionOutcome>
{
    public async Task<BulkActionOutcome> Handle(BulkLockUsersCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetUsersEligibleIdsQuery(request.Search, UserBulkAction.Lock), cancellationToken)
            : request.Target.Ids!;

        var succeeded = 0;
        var skipped = new List<BulkActionSkip>();

        foreach (var id in ids)
        {
            if (string.Equals(id, actorUserId, StringComparison.Ordinal))
            {
                skipped.Add(new BulkActionSkip(id, "Self"));
                continue;
            }

            var user = await userAdminRepository.GetByIdAsync(id, cancellationToken);
            if (user is null)
            {
                skipped.Add(new BulkActionSkip(id, "NotFound"));
                continue;
            }

            if (user.IsLockedOut)
            {
                skipped.Add(new BulkActionSkip(id, "AlreadyLocked"));
                continue;
            }

            try
            {
                await LastSuperUserGuard.EnsureNotStrandingSystemAsync(
                    identityService, id, actionRemovesTargetsSuperUserStatus: true, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                skipped.Add(new BulkActionSkip(id, "LastSuperUser"));
                continue;
            }

            await identityService.SetLockoutAsync(id, locked: true, cancellationToken);
            AdminActionLog.AdminUserActionPerformed(logger, "BulkLock", actorUserId, id, "Account locked (bulk)");
            succeeded++;
        }

        return new BulkActionOutcome(succeeded, skipped);
    }
}
