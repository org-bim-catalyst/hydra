using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.AdminSendPasswordReset;

public sealed class AdminSendPasswordResetCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    IPasswordResetIssuanceJob issuanceJob,
    ILogger<AdminSendPasswordResetCommandHandler> logger) : IRequestHandler<AdminSendPasswordResetCommand>
{
    public async Task Handle(AdminSendPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (string.Equals(request.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You cannot send yourself a password reset link from here.");
        }

        var eligibility = await identityService.GetPasswordResetEligibilityAsync(request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        if (!eligibility.EmailConfirmed)
        {
            throw new DomainRuleViolationException("This account's email is not confirmed yet; confirm it before sending a password reset link.");
        }

        if (eligibility.IsLockedOut)
        {
            throw new DomainRuleViolationException("This account is locked; unlock it before sending a password reset link.");
        }

        // Reuses the same eligibility/throttle/token-issuance logic as the self-service
        // "forgot password" flow (research.md Topic 3 equivalent) — no duplicate token plumbing.
        await issuanceJob.IssueAsync(eligibility.Email, requestedFromIp: null, cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "SendPasswordReset", actorUserId, request.UserId, "Password reset link sent");
    }
}
