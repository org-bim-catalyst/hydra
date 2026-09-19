using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authentication;

namespace AskLucy.Application.Authentication;

/// <summary>Shared token-issuance step used by Login/Login2fa/Refresh/ExternalLogin handlers.</summary>
public sealed class TokenIssuer(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<AuthResult> IssueAsync(
        string userId, IReadOnlyList<Claim> claims, Guid? rotatingFamilyId, CancellationToken cancellationToken)
    {
        var issuedRefreshToken = tokenService.IssueRefreshToken(rotatingFamilyId);

        // The access token names the session it belongs to, so revoking that session can be
        // enforced on the token itself rather than only on the next refresh. Without this claim a
        // "sign out my other devices" is honoured up to a whole access-token lifetime late — the
        // other browser keeps full API access until its JWT expires. See ISessionRevocationCache.
        var sessionClaims = claims
            .Append(new Claim(SessionClaims.SessionId, issuedRefreshToken.TokenFamilyId.ToString()))
            .ToList();

        var accessToken = tokenService.GenerateAccessToken(userId, sessionClaims);

        var refreshTokenEntity = RefreshToken.IssueNew(
            userId, issuedRefreshToken.Hash, issuedRefreshToken.TokenFamilyId, issuedRefreshToken.Lifetime);

        refreshTokenRepository.Add(refreshTokenEntity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            AuthOutcome.Success,
            userId,
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            issuedRefreshToken.PlainTextToken);
    }
}
