using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.LockUser;

public sealed class LockUserCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    ILogger<LockUserCommandHandler> logger) : IRequestHandler<LockUserCommand>
{
    public async Task Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (string.Equals(request.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You cannot lock your own account.");
        }

        await LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            identityService, request.UserId, actionRemovesTargetsSuperUserStatus: true, cancellationToken);

        await identityService.SetLockoutAsync(request.UserId, locked: true, cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "Lock", actorUserId, request.UserId, "Account locked");
    }
}
