using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

/// <summary>
/// Asks the notification hub to send the change-email confirmation to the new, unverified address
/// (specs/067 US9-B). The link, with its token, is minted when the email is sent. The template gets the
/// address masked, never in full.
/// </summary>
public sealed class RequestEmailChangeCommandHandler(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IRequestHandler<RequestEmailChangeCommand>
{
    public async Task Handle(RequestEmailChangeCommand request, CancellationToken cancellationToken)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountEmailChangeRequested,
            new NotificationRecipient.AddressForUser(request.UserId, request.NewEmail),
            new Dictionary<string, string?> { ["newEmailMasked"] = MaskAddress(request.NewEmail) }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The first letter of the local part and the whole domain: enough to recognise, not enough to harvest.</summary>
    internal static string MaskAddress(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "your new address" : $"{address[0]}***{address[at..]}";
    }
}
