using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Authentication.Commands.ResetPassword;

/// <summary>
/// Redeems a reset link (specs/058-password-recovery US2, FR-005 to FR-011).
/// <para>
/// Every rejection cause returns the same <see cref="PasswordResetOutcome.InvalidToken"/>; the
/// cause itself is logged, not returned (FR-015). That is capture-not-expose: nothing is
/// swallowed, and telling the caller which of "no such token" / "already used" / "expired" /
/// "email changed" applies would turn the endpoint into an oracle.
/// </para>
/// </summary>
public sealed partial class ResetPasswordCommandHandler(
    IPasswordResetTokenRepository resetTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IIdentityService identityService,
    ITokenService tokenService,
    ISessionRevocationCache sessionRevocationCache,
    IBackgroundJobClient backgroundJobClient,
    IUnitOfWork unitOfWork,
    ILogger<ResetPasswordCommandHandler> logger) : IRequestHandler<ResetPasswordCommand, PasswordResetResult>
{
    public async Task<PasswordResetResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var token = await resetTokenRepository.FindByHashAsync(tokenService.Hash(request.Token), cancellationToken);

        if (token is null)
        {
            LogRejected(logger, request.UserId, "unknown token", DateTime.UtcNow);
            return PasswordResetResult.InvalidToken;
        }

        if (!string.Equals(token.UserId, request.UserId, StringComparison.Ordinal))
        {
            // The link's user id was tampered with, or replayed against another account.
            LogRejected(logger, request.UserId, "token belongs to a different account", DateTime.UtcNow);
            return PasswordResetResult.InvalidToken;
        }

        var eligibility = await identityService.GetPasswordResetEligibilityAsync(token.UserId, cancellationToken);

        if (eligibility is null)
        {
            LogRejected(logger, token.UserId, "account no longer exists", DateTime.UtcNow);
            return PasswordResetResult.InvalidToken;
        }

        if (!token.CanRedeemFor(eligibility.Email))
        {
            LogRejected(logger, token.UserId, DescribeRejection(token, eligibility.Email), DateTime.UtcNow);
            return PasswordResetResult.InvalidToken;
        }

        // FR-014: an external-provider account redeeming a link is setting its first password, not
        // replacing one — Identity's remove-then-add path has nothing to remove.
        var reset = eligibility.HasPassword
            ? await identityService.ResetPasswordAsync(token.UserId, request.NewPassword, cancellationToken)
            : await identityService.SetPasswordAsync(token.UserId, request.NewPassword, cancellationToken);

        if (reset.Status != IdentityResultStatus.Success)
        {
            // The token is deliberately left redeemable: the user's password is unchanged, and
            // making them request a fresh link because they typed a too-short password would be
            // punitive. The per-IP limiter still bounds retries.
            LogPolicyRejected(logger, token.UserId, DateTime.UtcNow);
            return new PasswordResetResult(PasswordResetOutcome.PasswordPolicyViolation, reset.Errors ?? []);
        }

        token.Consume();

        // Any other outstanding link is now stale — the password it was issued against is gone.
        await resetTokenRepository.SupersedePendingForUserAsync(token.UserId, token.Id, cancellationToken);

        // FR-009: a reset is the "someone else may have my account" path, so every session goes,
        // including the one redeeming the link. Same unit of work as the password write below, so
        // the reset can never half-apply.
        var activeSessions = await refreshTokenRepository.ListActiveByUserAsync(token.UserId, cancellationToken);

        foreach (var session in activeSessions)
        {
            session.Revoke();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The access tokens those sessions already hold stay valid on their own signature until
        // they expire, so revoking the refresh token is only half of signing them out.
        foreach (var familyId in activeSessions.Select(s => s.TokenFamilyId).Distinct())
        {
            sessionRevocationCache.Evict(familyId);
        }

        var email = eligibility.Email;
        var changedAtUtc = DateTime.UtcNow;

        backgroundJobClient.Enqueue<IPasswordEmailJob>(
            j => j.SendPasswordChangedNoticeAsync(email, changedAtUtc, CancellationToken.None));

        LogResetCompleted(logger, token.UserId, activeSessions.Count, changedAtUtc);

        return PasswordResetResult.Success;
    }

    private static string DescribeRejection(Domain.Authentication.PasswordResetToken token, string currentEmail) =>
        token.ConsumedAtUtc is not null ? "token already used"
        : token.SupersededAtUtc is not null ? "token superseded by a newer request or a password change"
        : DateTime.UtcNow >= token.ExpiresAtUtc ? "token expired"
        : !string.Equals(token.EmailAtIssue, currentEmail, StringComparison.OrdinalIgnoreCase) ? "account email changed since the link was issued"
        : "token not redeemable";

    [LoggerMessage(EventId = 5820, Level = LogLevel.Information, Message = "Security: password reset completed. UserId={UserId} SessionsRevoked={SessionsRevoked} AtUtc={AtUtc}")]
    private static partial void LogResetCompleted(ILogger logger, string userId, int sessionsRevoked, DateTime atUtc);

    [LoggerMessage(EventId = 5821, Level = LogLevel.Warning, Message = "Security: password reset rejected. UserId={UserId} Cause={Cause} AtUtc={AtUtc}")]
    private static partial void LogRejected(ILogger logger, string userId, string cause, DateTime atUtc);

    [LoggerMessage(EventId = 5822, Level = LogLevel.Information, Message = "Password reset refused: the new password did not satisfy the policy. UserId={UserId} AtUtc={AtUtc}")]
    private static partial void LogPolicyRejected(ILogger logger, string userId, DateTime atUtc);
}
