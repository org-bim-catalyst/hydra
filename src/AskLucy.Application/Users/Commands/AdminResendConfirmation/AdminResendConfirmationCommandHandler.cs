using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Users.Commands.AdminResendConfirmation;

public sealed class AdminResendConfirmationCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork,
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

        // Straight to the account, by id: an administrator already knows who they mean, so there is nothing to look up.
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountEmailConfirmationRequested,
            new NotificationRecipient.AddressForUser(request.UserId, eligibility.Email),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "ResendConfirmationEmail", actorUserId, request.UserId, "Confirmation email resent");
    }
}
