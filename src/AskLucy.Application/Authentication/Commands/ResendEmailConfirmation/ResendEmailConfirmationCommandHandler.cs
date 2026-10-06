using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;

/// <summary>
/// Publishes the address as typed and returns, for the reason spelled out in
/// <c>RequestPasswordResetCommandHandler</c>: every lookup that could tell "this address has an unconfirmed
/// account" apart from "this address has none" is work whose duration would undo the neutral 202. The hub
/// resolves the address in the background (specs/067 US9-B, FR-009e).
/// </summary>
public sealed class ResendEmailConfirmationCommandHandler(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IRequestHandler<ResendEmailConfirmationCommand>
{
    public async Task Handle(ResendEmailConfirmationCommand request, CancellationToken cancellationToken)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountEmailConfirmationRequested,
            new NotificationRecipient.AddressLookup(request.Email.Trim()),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
