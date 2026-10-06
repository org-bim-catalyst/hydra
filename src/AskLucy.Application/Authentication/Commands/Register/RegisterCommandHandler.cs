using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.Register;

/// <summary>
/// Registers the account and asks the notification hub for its confirmation email (specs/067 US9-B). The
/// hub renders the branded email from the <c>account.email-confirmation.requested</c> template and mints
/// the one-time link when it sends, so no token or link is built, stored or logged here. The address counts as
/// routable although it is unverified: confirming it is what the email is for (FR-009c).
/// </summary>
public sealed class RegisterCommandHandler(
    IIdentityService identityService,
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IRequestHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.RegisterAsync(
            request.Email, request.Password, request.FirstName, request.LastName, cancellationToken);

        if (result.Status != IdentityResultStatus.Success || result.UserId is null)
        {
            return new AuthResult(AuthOutcome.Failed, Errors: result.Errors);
        }

        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountEmailConfirmationRequested,
            new NotificationRecipient.AddressForUser(result.UserId, request.Email),
            new Dictionary<string, string?>(),
            EventKey: $"account.email-confirmation:{result.UserId}:registration"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(AuthOutcome.Success, result.UserId);
    }
}
