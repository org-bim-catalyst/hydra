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
        var accessToken = tokenService.GenerateAccessToken(userId, claims);
        var issuedRefreshToken = tokenService.IssueRefreshToken(rotatingFamilyId);

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
