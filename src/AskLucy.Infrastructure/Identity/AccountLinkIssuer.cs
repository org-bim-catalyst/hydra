using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Identity;

/// <summary>
/// Mints the one-time link of an account email at send time (specs/067 research R10, FR-009d), with the
/// same token providers and single-use persistence the Hangfire jobs used before the hub: Identity's
/// confirmation and change-email tokens, and the spec-058 hashed <see cref="PasswordResetToken"/>. Only
/// the timing moved, from the request to the send. Nothing of the link is stored by the hub or logged: the
/// <see cref="Uri"/> goes straight to the renderer.
/// <para>
/// An account that mustn't be sent a link (unconfirmed, locked out, throttled, already confirmed) is refused
/// with one neutral message, whatever the cause; the cause is logged to the security log with the user id.
/// That is capture-not-expose, as in <c>PasswordResetIssuanceJob</c>: nothing is swallowed, and nothing
/// tells the requester whether the address has an account (FR-009e).
/// </para>
/// </summary>
public sealed partial class AccountLinkIssuer(
    IIdentityService identityService,
    IPasswordResetTokenRepository resetTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IOptions<AppOptions> appOptions,
    ILogger<AccountLinkIssuer> logger) : IAccountLinkIssuer
{
    /// <summary>Long enough for email delivery and a distracted user, short enough to limit an inbox's exposure (spec 058).</summary>
    internal static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(15);

    private const int MaxResetRequestsPerWindow = 3;

    private const string RefusedMessage = "No link was issued for this request.";

    public async Task<Uri> IssueAsync(
        SensitiveLinkKind kind, string userId, string targetAddress, bool isRetry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAddress);

        return kind switch
        {
            SensitiveLinkKind.EmailConfirmation => await IssueConfirmationAsync(userId, cancellationToken),
            SensitiveLinkKind.EmailChange => await IssueEmailChangeAsync(userId, targetAddress, cancellationToken),
            SensitiveLinkKind.PasswordReset => await IssueResetAsync(userId, isRetry, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account link kind."),
        };
    }

    private async Task<Uri> IssueConfirmationAsync(string userId, CancellationToken cancellationToken)
    {
        var eligibility = await identityService.GetPasswordResetEligibilityAsync(userId, cancellationToken);
        if (eligibility is null)
        {
            throw Refuse(userId, "the account no longer exists");
        }

        if (eligibility.EmailConfirmed)
        {
            throw Refuse(userId, "the account is already confirmed");
        }

        var token = await identityService.GenerateEmailConfirmationTokenAsync(userId, cancellationToken);
        LogLinkIssued(logger, SensitiveLinkKind.EmailConfirmation, userId);
        return new Uri($"{BaseUrl()}/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}");
    }

    private async Task<Uri> IssueEmailChangeAsync(string userId, string newEmail, CancellationToken cancellationToken)
    {
        var token = await identityService.GenerateChangeEmailTokenAsync(userId, newEmail, cancellationToken);
        LogLinkIssued(logger, SensitiveLinkKind.EmailChange, userId);
        return new Uri(
            $"{BaseUrl()}/confirm-email-change" +
            $"?userId={Uri.EscapeDataString(userId)}" +
            $"&newEmail={Uri.EscapeDataString(newEmail)}" +
            $"&token={Uri.EscapeDataString(token)}");
    }

    private async Task<Uri> IssueResetAsync(string userId, bool isRetry, CancellationToken cancellationToken)
    {
        var eligibility = await identityService.GetPasswordResetEligibilityAsync(userId, cancellationToken);
        if (eligibility is null)
        {
            throw Refuse(userId, "the account no longer exists");
        }

        if (!eligibility.EmailConfirmed)
        {
            // Resetting an unconfirmed address would turn this flow into an email-verification bypass:
            // anyone who registered with someone else's address could take it over.
            throw Refuse(userId, "the email address was never confirmed");
        }

        if (eligibility.IsLockedOut)
        {
            throw Refuse(userId, "the account is locked out");
        }

        if (!isRetry)
        {
            // A retry of a delivery already accepted is the same request, not a new one; only a new request
            // counts toward the limit. Silent rather than a 429: a throttle keyed to an address is itself an
            // account-existence oracle (spec 058 research Topic 3).
            var issuedRecently = await resetTokenRepository.CountIssuedSinceAsync(userId, DateTime.UtcNow - ThrottleWindow, cancellationToken);
            if (issuedRecently >= MaxResetRequestsPerWindow)
            {
                throw Refuse(userId, $"too many reset links were issued recently ({issuedRecently} in {ThrottleWindow.TotalMinutes:0} minutes)");
            }
        }

        // A newly requested link invalidates its predecessors, so a forwarded or intercepted older email can't
        // still be redeemed (FR-006).
        await resetTokenRepository.SupersedePendingForUserAsync(userId, cancellationToken: cancellationToken);

        var plaintextToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        resetTokenRepository.Add(PasswordResetToken.IssueNew(userId, tokenService.Hash(plaintextToken), eligibility.Email, ResetLifetime));

        // Saved before the link is returned, so a crash between the send and the next save can never leave an
        // email whose link the server doesn't know. Only the hash is stored.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogLinkIssued(logger, SensitiveLinkKind.PasswordReset, userId);
        return new Uri($"{BaseUrl()}/reset-password?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(plaintextToken)}");
    }

    private string BaseUrl() => appOptions.Value.FrontendBaseUrl.TrimEnd('/');

    private AccountLinkRefusedException Refuse(string userId, string reason)
    {
        LogLinkRefused(logger, userId, reason);
        return new AccountLinkRefusedException(RefusedMessage);
    }

    [LoggerMessage(EventId = 5841, Level = LogLevel.Information, Message = "Security: {Kind} link issued at send time. UserId={UserId}")]
    private static partial void LogLinkIssued(ILogger logger, SensitiveLinkKind kind, string userId);

    [LoggerMessage(EventId = 5842, Level = LogLevel.Information, Message = "Security: account link not issued, {Reason}. UserId={UserId}")]
    private static partial void LogLinkRefused(ILogger logger, string userId, string reason);
}
