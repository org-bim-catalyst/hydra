using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.BulkForceReset2fa;

public sealed class BulkForceReset2faCommandHandler(
    ISender mediator,
    IIdentityService identityService,
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser,
    ILogger<BulkForceReset2faCommandHandler> logger) : IRequestHandler<BulkForceReset2faCommand, BulkActionOutcome>
{
    public async Task<BulkActionOutcome> Handle(BulkForceReset2faCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetUsersEligibleIdsQuery(request.Search, UserBulkAction.ForceReset2fa), cancellationToken)
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

            await identityService.DisableTwoFactorAsync(id, cancellationToken);
            AdminActionLog.AdminUserActionPerformed(logger, "BulkForceReset2fa", actorUserId, id, "Two-factor authentication reset (bulk)");
            succeeded++;
        }

        return new BulkActionOutcome(succeeded, skipped);
    }
}
