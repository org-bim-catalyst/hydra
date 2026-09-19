using AskLucy.Domain.Authentication;

namespace AskLucy.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(Guid tokenFamilyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every non-revoked, non-expired token for this user — i.e. their live sessions. Used to sign
    /// a user out everywhere after a password reset (FR-009) or everywhere but the acting session
    /// after a password change (FR-010), specs/058-password-recovery.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> ListActiveByUserAsync(string userId, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);
}
