using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.BulkDeleteUsers;

public sealed class BulkDeleteUsersCommandHandler(
    ISender mediator,
    IIdentityService identityService,
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser,
    ILogger<BulkDeleteUsersCommandHandler> logger) : IRequestHandler<BulkDeleteUsersCommand, BulkActionOutcome>
{
    public async Task<BulkActionOutcome> Handle(BulkDeleteUsersCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var ids = request.Target.AllMatching
            ? await mediator.Send(new GetUsersEligibleIdsQuery(request.Search, UserBulkAction.Delete), cancellationToken)
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

            var deleted = await userAdminRepository.DeleteAsync(id, actorUserId, cancellationToken);
            if (!deleted)
            {
                skipped.Add(new BulkActionSkip(id, "NotFound"));
                continue;
            }

            AdminActionLog.AdminUserActionPerformed(logger, "BulkDelete", actorUserId, id, "Account soft-deleted (bulk)");
            succeeded++;
        }

        return new BulkActionOutcome(succeeded, skipped);
    }
}
