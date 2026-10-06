using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Hangfire;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// A one-release forwarding shim (specs/067 US9-B, T133). The two emails a signed-out visitor can trigger from
/// the sign-in page used to be built and sent here from a Hangfire worker. The hub now does both. This class
/// stays only so a job enqueued with the old signature before the deploy still runs: each method hands the
/// equivalent request to the hub and saves.
/// </summary>
[Obsolete("Removed in the release after 067; forwards to the notification hub.")]
[AutomaticRetry(Attempts = 3)]
public sealed class AccountEmailJob(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IAccountEmailJob
{
    public async Task ResendConfirmationAsync(string email, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountEmailConfirmationRequested,
            new NotificationRecipient.AddressLookup(email.Trim()),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SendAccountSupportRequestAsync(
        string fromEmail, string message, string? requestedFromIp, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountSupportRequestSubmitted,
            new NotificationRecipient.SupportMailbox(),
            new Dictionary<string, string?>
            {
                ["requesterEmail"] = fromEmail,
                ["requestKind"] = "access",
                ["messageBody"] = message,
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
