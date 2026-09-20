using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.AdminResendConfirmation;

public sealed class AdminResendConfirmationCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    IAccountEmailJob accountEmailJob,
    ILogger<AdminResendConfirmationCommandHandler> logger) : IRequestHandler<AdminResendConfirmationCommand>
{
    public async Task Handle(AdminResendConfirmationCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (string.Equals(request.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You cannot resend yourself a confirmation email from here.");
        }

        // GetPasswordResetEligibilityAsync is the existing id->email/confirmation-status lookup;
        // reused here rather than adding a near-duplicate query to IIdentityService.
        var eligibility = await identityService.GetPasswordResetEligibilityAsync(request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        if (eligibility.EmailConfirmed)
        {
            throw new DomainRuleViolationException("This account's email is already confirmed.");
        }

        await accountEmailJob.ResendConfirmationAsync(eligibility.Email, cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "ResendConfirmationEmail", actorUserId, request.UserId, "Confirmation email resent");
    }
}
