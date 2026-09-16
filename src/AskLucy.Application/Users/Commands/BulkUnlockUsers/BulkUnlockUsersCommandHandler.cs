using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.BulkUnlockUsers;

public sealed class BulkUnlockUsersCommandHandler(
    ISender mediator,
    IIdentityService identityService,
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser,
    ILogger<BulkUnlockUsersCommandHandler> logger) : IRequestHandler<BulkUnlockUsersCommand, BulkActionOutcome>
{
    public async Task<BulkActionOutcome> Handle(BulkUnlockUsersCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetUsersEligibleIdsQuery(request.Search, UserBulkAction.Unlock), cancellationToken)
            : request.Target.Ids!;

        var succeeded = 0;
        var skipped = new List<BulkActionSkip>();

        foreach (var id in ids)
        {
            var user = await userAdminRepository.GetByIdAsync(id, cancellationToken);
            if (user is null)
            {
                skipped.Add(new BulkActionSkip(id, "NotFound"));
                continue;
            }

            await identityService.SetLockoutAsync(id, locked: false, cancellationToken);
            AdminActionLog.AdminUserActionPerformed(logger, "BulkUnlock", actorUserId, id, "Account unlocked (bulk)");
            succeeded++;
        }

        return new BulkActionOutcome(succeeded, skipped);
    }
}
