using MediatR;

namespace AskLucy.Application.Authentication.Queries.ValidatePasswordResetToken;

/// <summary>
/// Whether an emailed reset link is still redeemable, checked when the reset page loads so a
/// spent or expired link says so up front instead of only after the user has chosen, typed and
/// confirmed a new password (specs/058-password-recovery US2).
/// <para>
/// This is no more of an oracle than posting a throwaway password to the redeem endpoint would
/// be — both need the caller to already hold the link's user id and token, and both share the
/// same per-IP rate limiter. It deliberately answers a single boolean: which of "unknown" /
/// "already used" / "expired" / "email changed" applies stays in the log (FR-015).
/// </para>
/// </summary>
public sealed record ValidatePasswordResetTokenQuery(string UserId, string Token) : IRequest<bool>;
