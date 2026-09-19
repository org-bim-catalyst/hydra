using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authentication;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Authentication.PasswordReset;

/// <summary>
/// Implements <see cref="IPasswordResetIssuanceJob"/> — see that interface for why this runs on a
/// worker rather than on the request thread.
/// <para>
/// An unknown address, an unconfirmed one, a locked-out account and a throttled requester all end
/// the same way: nothing is sent, and the real reason goes to the security log (FR-003, FR-015).
/// That is capture-not-expose, not a silent failure — nothing is swallowed, and the user's next
/// step is the same in every case.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed partial class PasswordResetIssuanceJob(
    IIdentityService identityService,
    IPasswordResetTokenRepository resetTokenRepository,
    ITokenService tokenService,
    IPasswordTokenProtector tokenProtector,
    IBackgroundJobClient backgroundJobClient,
    IUnitOfWork unitOfWork,
    ILogger<PasswordResetIssuanceJob> logger) : IPasswordResetIssuanceJob
{
    /// <summary>Long enough for email delivery and a distracted user, short enough to limit an inbox's exposure (spec.md § Assumptions).</summary>
    internal static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(15);

    private const int MaxRequestsPerWindow = 3;

    public async Task IssueAsync(string email, string? requestedFromIp, CancellationToken cancellationToken = default)
    {
        email = email.Trim();

        var userId = await identityService.FindIdByEmailAsync(email, cancellationToken);
        if (userId is null)
        {
            LogRequestForUnknownAddress(logger, DateTime.UtcNow, requestedFromIp);
            return;
        }

        var eligibility = await identityService.GetPasswordResetEligibilityAsync(userId, cancellationToken);
        if (eligibility is null)
        {
            LogRequestForUnknownAddress(logger, DateTime.UtcNow, requestedFromIp);
            return;
        }

        if (!eligibility.EmailConfirmed)
        {
            // Resetting an unconfirmed address would turn this flow into an email-verification
            // bypass: anyone who registered with someone else's address could take it over.
            LogRequestForUnconfirmedAccount(logger, userId, DateTime.UtcNow, requestedFromIp);
            return;
        }

        if (eligibility.IsLockedOut)
        {
            LogRequestForLockedOutAccount(logger, userId, DateTime.UtcNow, requestedFromIp);
            return;
        }

        var issuedRecently = await resetTokenRepository.CountIssuedSinceAsync(
            userId, DateTime.UtcNow.Subtract(ThrottleWindow), cancellationToken);

        if (issuedRecently >= MaxRequestsPerWindow)
        {
            // Silently, not as a 429: a visible throttle keyed to an email address is itself an
            // account-existence oracle (research.md Topic 3). The IP-partitioned limiter in
            // Program.cs is the one allowed to be visible, because it says nothing about accounts.
            LogResetThrottled(logger, userId, issuedRecently, DateTime.UtcNow, requestedFromIp);
            return;
        }

        // A newly requested link invalidates its predecessors, so a forwarded or intercepted older
        // email cannot still be redeemed (FR-006).
        await resetTokenRepository.SupersedePendingForUserAsync(userId, cancellationToken: cancellationToken);

        var plaintextToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        resetTokenRepository.Add(PasswordResetToken.IssueNew(
            userId,
            tokenService.Hash(plaintextToken),
            eligibility.Email,
            TokenLifetime,
            requestedFromIp));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Enqueued rather than awaited: an SMTP round trip inside the request would make response
        // time depend on whether the address has an account (FR-003). Protected before it leaves
        // this method, because the job argument is serialised into Hangfire's SQL job store and
        // plaintext there would put a usable link in the same database that deliberately stores
        // only hashes (FR-016, research.md Topic 5a).
        var protectedToken = tokenProtector.Protect(plaintextToken);

        backgroundJobClient.Enqueue<IPasswordEmailJob>(
            j => j.SendResetLinkAsync(userId, eligibility.Email, protectedToken, CancellationToken.None));

        LogResetRequested(logger, userId, DateTime.UtcNow, requestedFromIp);
    }

    [LoggerMessage(EventId = 5801, Level = LogLevel.Information, Message = "Security: password reset requested. UserId={UserId} AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogResetRequested(ILogger logger, string userId, DateTime atUtc, string? origin);

    [LoggerMessage(EventId = 5802, Level = LogLevel.Information, Message = "Security: password reset requested for an address with no account. AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogRequestForUnknownAddress(ILogger logger, DateTime atUtc, string? origin);

    [LoggerMessage(EventId = 5803, Level = LogLevel.Information, Message = "Security: password reset refused, email never confirmed. UserId={UserId} AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogRequestForUnconfirmedAccount(ILogger logger, string userId, DateTime atUtc, string? origin);

    [LoggerMessage(EventId = 5804, Level = LogLevel.Information, Message = "Security: password reset refused, account locked out. UserId={UserId} AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogRequestForLockedOutAccount(ILogger logger, string userId, DateTime atUtc, string? origin);

    [LoggerMessage(EventId = 5805, Level = LogLevel.Warning, Message = "Security: password reset throttled. UserId={UserId} IssuedInWindow={IssuedInWindow} AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogResetThrottled(ILogger logger, string userId, int issuedInWindow, DateTime atUtc, string? origin);
}
