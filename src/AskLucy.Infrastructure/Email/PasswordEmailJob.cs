using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Hangfire;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// A one-release forwarding shim (specs/067 US9-B, T133). The two password-flow emails used to be built and
/// sent here from a Hangfire worker (specs/058-password-recovery). The hub now renders and sends them, and mints
/// the reset link when it sends. This class stays only so a job enqueued with the old signature before the
/// deploy still runs: each method hands the equivalent request to the hub and saves.
/// </summary>
[Obsolete("Removed in the release after 067; forwards to the notification hub.")]
[AutomaticRetry(Attempts = 3)]
public sealed class PasswordEmailJob(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork) : IPasswordEmailJob
{
    /// <remarks>
    /// <paramref name="protectedToken"/> is deliberately unused: it names a link that was issued before the
    /// deploy. Asking the hub for a new one supersedes it (FR-006), so the user ends with exactly one working link.
    /// </remarks>
    public async Task SendResetLinkAsync(string userId, string email, string protectedToken, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountPasswordResetRequested,
            new NotificationRecipient.User(userId),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SendPasswordChangedNoticeAsync(string email, DateTime changedAtUtc, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.SecurityPasswordChanged,
            new NotificationRecipient.AddressLookup(email.Trim()),
            new Dictionary<string, string?> { ["changedAt"] = $"{changedAtUtc:yyyy-MM-dd HH:mm} UTC" }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
