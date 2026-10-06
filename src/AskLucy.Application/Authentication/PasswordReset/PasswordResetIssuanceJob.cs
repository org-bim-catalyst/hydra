using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Hangfire;

namespace AskLucy.Application.Authentication.PasswordReset;

/// <summary>
/// A one-release forwarding shim (specs/067 US9-B, T133). Reset requests used to be issued here, on a Hangfire
/// worker, so that the request path cost the same for every address (FR-003). The hub now does that, and the
/// eligibility, throttle and token issuance moved to the account link issuer, which runs when the email is
/// sent. This class stays only so a job enqueued with the old signature before the deploy still runs: it
/// hands the request to the hub and saves.
/// </summary>
[Obsolete("Removed in the release after 067; forwards to the notification hub.")]
[AutomaticRetry(Attempts = 3)]
public sealed class PasswordResetIssuanceJob(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IPasswordResetIssuanceJob
{
    public async Task IssueAsync(string email, string? requestedFromIp, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountPasswordResetRequested,
            new NotificationRecipient.AddressLookup(email.Trim()),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
