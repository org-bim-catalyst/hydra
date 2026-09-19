using AskLucy.Domain.Authentication;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Storage for <see cref="PasswordResetToken"/> (specs/058-password-recovery). Lookups are by
/// hash only — the plaintext token exists solely in the email that carries it.
/// </summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Returns the token with this hash regardless of its state. Callers MUST check
    /// <see cref="PasswordResetToken.CanRedeemFor"/> rather than assume a hit is redeemable —
    /// distinguishing "no such token" from "spent token" is exactly what the caller must not
    /// leak to the user (FR-005/FR-006).
    /// </summary>
    Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Tokens issued for this user since <paramref name="sinceUtc"/>, for the per-email throttle (FR-004).</summary>
    Task<int> CountIssuedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every pending token for this user superseded, so a newly issued link invalidates its
    /// predecessors and any password change invalidates outstanding links (FR-006).
    /// <paramref name="exceptTokenId"/> spares the token being redeemed, so its audit row records
    /// that it was used rather than invalidated.
    /// </summary>
    Task SupersedePendingForUserAsync(string userId, Guid? exceptTokenId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes tokens created before <paramref name="createdBeforeUtc"/> that can no longer be
    /// redeemed, up to <paramref name="batchSize"/> rows, and returns how many went. Retention,
    /// not correctness: a spent token is already inert, but keeping its row forever would leave a
    /// per-user record of every recovery attempt with no purpose to serve.
    /// </summary>
    Task<int> DeleteSpentBeforeAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default);

    void Add(PasswordResetToken token);
}
