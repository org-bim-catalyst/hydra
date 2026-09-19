using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Authentication.Commands.ChangePassword;

/// <summary>
/// specs/058-password-recovery US3/US4. Unlike a reset, this runs for someone who has already
/// proved who they are, so their own session survives (FR-010) — every other one still goes, on the
/// assumption that a deliberate password change may be a response to a suspected compromise.
/// </summary>
public sealed partial class ChangePasswordCommandHandler(
    IIdentityService identityService,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordResetTokenRepository resetTokenRepository,
    ITokenService tokenService,
    IBackgroundJobClient backgroundJobClient,
    IUnitOfWork unitOfWork,
    ILogger<ChangePasswordCommandHandler> logger) : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var eligibility = await identityService.GetPasswordResetEligibilityAsync(request.UserId, cancellationToken);

        if (eligibility is null)
        {
            // An authenticated caller whose account has vanished: nothing to change, and nothing
            // the caller can do about it either.
            LogAccountMissing(logger, request.UserId, DateTime.UtcNow);
            return new ChangePasswordResult(ChangePasswordOutcome.CurrentPasswordIncorrect);
        }

        if (eligibility.HasPassword)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return new ChangePasswordResult(ChangePasswordOutcome.CurrentPasswordRequired);
            }

            if (!await identityService.VerifyPasswordAsync(request.UserId, request.CurrentPassword, cancellationToken))
            {
                LogWrongCurrentPassword(logger, request.UserId, DateTime.UtcNow);
                return new ChangePasswordResult(ChangePasswordOutcome.CurrentPasswordIncorrect);
            }

            if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
            {
                return new ChangePasswordResult(ChangePasswordOutcome.SameAsCurrentPassword);
            }
        }

        // FR-014: a password-less external-provider account is setting its first password, not
        // replacing one, and Identity's remove-then-add path does not apply.
        var applied = eligibility.HasPassword
            ? await identityService.ResetPasswordAsync(request.UserId, request.NewPassword, cancellationToken)
            : await identityService.SetPasswordAsync(request.UserId, request.NewPassword, cancellationToken);

        if (applied.Status != IdentityResultStatus.Success)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.PasswordPolicyViolation, applied.Errors ?? []);
        }

        // FR-006: any reset link still sitting in an inbox was issued against the old password.
        await resetTokenRepository.SupersedePendingForUserAsync(request.UserId, cancellationToken: cancellationToken);

        var actingFamilyId = await ResolveActingFamilyIdAsync(request.ActingRefreshToken, cancellationToken);
        var revoked = 0;

        foreach (var session in await refreshTokenRepository.ListActiveByUserAsync(request.UserId, cancellationToken))
        {
            if (actingFamilyId is not null && session.TokenFamilyId == actingFamilyId)
            {
                continue;
            }

            session.Revoke();
            revoked++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var email = eligibility.Email;
        var changedAtUtc = DateTime.UtcNow;

        backgroundJobClient.Enqueue<IPasswordEmailJob>(
            j => j.SendPasswordChangedNoticeAsync(email, changedAtUtc, CancellationToken.None));

        LogPasswordChanged(logger, request.UserId, revoked, changedAtUtc);

        return ChangePasswordResult.Success;
    }

    private async Task<Guid?> ResolveActingFamilyIdAsync(string? actingRefreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(actingRefreshToken))
        {
            return null;
        }

        var acting = await refreshTokenRepository.FindByHashAsync(tokenService.Hash(actingRefreshToken), cancellationToken);

        return acting?.TokenFamilyId;
    }

    [LoggerMessage(EventId = 5830, Level = LogLevel.Information, Message = "Security: password changed by the account holder. UserId={UserId} OtherSessionsRevoked={OtherSessionsRevoked} AtUtc={AtUtc}")]
    private static partial void LogPasswordChanged(ILogger logger, string userId, int otherSessionsRevoked, DateTime atUtc);

    [LoggerMessage(EventId = 5831, Level = LogLevel.Warning, Message = "Security: password change refused, current password incorrect. UserId={UserId} AtUtc={AtUtc}")]
    private static partial void LogWrongCurrentPassword(ILogger logger, string userId, DateTime atUtc);

    [LoggerMessage(EventId = 5832, Level = LogLevel.Warning, Message = "Password change requested for an account that no longer exists. UserId={UserId} AtUtc={AtUtc}")]
    private static partial void LogAccountMissing(ILogger logger, string userId, DateTime atUtc);
}
