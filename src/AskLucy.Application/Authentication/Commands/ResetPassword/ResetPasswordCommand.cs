using MediatR;

namespace AskLucy.Application.Authentication.Commands.ResetPassword;

/// <summary>
/// Redeems an emailed reset link (specs/058-password-recovery US2). <paramref name="Token"/> is the
/// plaintext token from the link, already URL-decoded by the caller.
/// </summary>
public sealed record ResetPasswordCommand(string UserId, string Token, string NewPassword)
    : IRequest<PasswordResetResult>;

public enum PasswordResetOutcome
{
    Success,

    /// <summary>
    /// The link is unknown, consumed, superseded, expired, belongs to another account, or was
    /// issued to an address the account no longer has. Deliberately one value for all six: the
    /// caller must not be able to tell them apart (FR-005/FR-006).
    /// </summary>
    InvalidToken,

    /// <summary>Identity rejected the new password; <see cref="PasswordResetResult.Errors"/> says which rules failed (FR-007).</summary>
    PasswordPolicyViolation,
}

public sealed record PasswordResetResult(PasswordResetOutcome Outcome, IReadOnlyList<string>? Errors = null)
{
    public static PasswordResetResult Success { get; } = new(PasswordResetOutcome.Success);

    public static PasswordResetResult InvalidToken { get; } = new(PasswordResetOutcome.InvalidToken);
}
