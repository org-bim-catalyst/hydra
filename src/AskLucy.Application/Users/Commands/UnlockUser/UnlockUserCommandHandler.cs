using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.UnlockUser;

public sealed class UnlockUserCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    ILogger<UnlockUserCommandHandler> logger) : IRequestHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        await identityService.SetLockoutAsync(request.UserId, locked: false, cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "Unlock", actorUserId, request.UserId, "Account unlocked");
    }
}
