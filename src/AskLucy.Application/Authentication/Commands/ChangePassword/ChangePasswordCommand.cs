using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangePassword;

/// <summary>
/// Changes the password of a signed-in account (specs/058-password-recovery US3/US4).
/// <para>
/// <paramref name="CurrentPassword"/> is optional because an account created through an external
/// provider has no password to confirm (FR-014); it is still required whenever one exists.
/// <paramref name="ActingRefreshToken"/> is the caller's own refresh token, so their session can be
/// spared while every other one is revoked (FR-010).
/// </para>
/// </summary>
public sealed record ChangePasswordCommand(
    string UserId,
    string? CurrentPassword,
    string NewPassword,
    string? ActingRefreshToken = null) : IRequest<ChangePasswordResult>;

public enum ChangePasswordOutcome
{
    Success,
    CurrentPasswordIncorrect,
    CurrentPasswordRequired,
    SameAsCurrentPassword,
    PasswordPolicyViolation,
}

public sealed record ChangePasswordResult(ChangePasswordOutcome Outcome, IReadOnlyList<string>? Errors = null)
{
    public static ChangePasswordResult Success { get; } = new(ChangePasswordOutcome.Success);
}
