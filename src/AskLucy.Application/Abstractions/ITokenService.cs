using System.Security.Claims;

namespace AskLucy.Application.Abstractions;

public sealed record AccessTokenResult(string AccessToken, DateTime ExpiresAtUtc);

public sealed record IssuedRefreshToken(string PlainTextToken, string Hash, Guid TokenFamilyId, TimeSpan Lifetime);

/// <summary>
/// Pure JWT access-token and refresh-token issuance (research.md Topic 1). Deliberately
/// takes only primitive/claims input — never <c>ApplicationUser</c> — so it can live in
/// Infrastructure without depending on the Persistence-owned Identity model.
/// </summary>
public interface ITokenService
{
    AccessTokenResult GenerateAccessToken(string userId, IEnumerable<Claim> claims);

    /// <summary>
    /// Same as <see cref="GenerateAccessToken(string, IEnumerable{Claim})"/>, but with an
    /// explicit expiry instead of the configured default (specs/060-hangfire-dashboard-access
    /// research.md Decision 4 — the Hangfire dashboard session needs a longer window than the
    /// SPA's normal access token).
    /// </summary>
    AccessTokenResult GenerateAccessToken(string userId, IEnumerable<Claim> claims, TimeSpan lifetime);

    IssuedRefreshToken IssueRefreshToken(Guid? existingTokenFamilyId = null);

    string Hash(string plainTextToken);
}
