using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Users.Commands.AdminSendPasswordReset;

public sealed class AdminSendPasswordResetCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork,
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

        // The same hub path as the self-service "forgot password" flow: the link is minted, throttled and
        // checked for eligibility when the email is sent, so there is no duplicate token plumbing here.
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountPasswordResetRequested,
            new NotificationRecipient.User(request.UserId),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AdminActionLog.AdminUserActionPerformed(logger, "SendPasswordReset", actorUserId, request.UserId, "Password reset link sent");
    }
}
