using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.ForceReset2fa;

public sealed class ForceReset2faCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    ILogger<ForceReset2faCommandHandler> logger) : IRequestHandler<ForceReset2faCommand>
{
    public async Task Handle(ForceReset2faCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (string.Equals(request.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You cannot force-reset your own two-factor authentication.");
        }

        // Reuses the same operation the user's own Settings-page "disable 2FA" action calls
        // (research.md Topic 3) — no duplicate Identity plumbing.
        await identityService.DisableTwoFactorAsync(request.UserId, cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "ForceReset2fa", actorUserId, request.UserId, "Two-factor authentication reset");
    }
}
