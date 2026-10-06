using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.RequestAccountSupport;

/// <summary>
/// Hands a locked-out user's message to the notification hub, addressed to the support mailbox
/// (specs/067 US9-B). The mailbox is server configuration read at send time, so the page can offer "contact
/// an administrator" without ever publishing an address to harvest.
/// </summary>
public sealed class RequestAccountSupportCommandHandler(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IRequestHandler<RequestAccountSupportCommand>
{
    public async Task Handle(RequestAccountSupportCommand request, CancellationToken cancellationToken)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountSupportRequestSubmitted,
            new NotificationRecipient.SupportMailbox(),
            new Dictionary<string, string?>
            {
                ["requesterEmail"] = request.Email,
                ["requestKind"] = "access",
                ["messageBody"] = request.Message,
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
