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

    /// <summary>
    /// Whether this session still has at least one live refresh token. Rotation keeps a family
    /// alive (the old token is revoked and its replacement added in the same commit), so this goes
    /// false only when the session itself is ended — logout, a reset/change signing other devices
    /// out, or reuse detection killing the family. Read on every authenticated request via a cache,
    /// so it must stay a single indexed lookup and never materialise the rows.
    /// </summary>
    Task<bool> IsFamilyActiveAsync(Guid tokenFamilyId, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);
}
