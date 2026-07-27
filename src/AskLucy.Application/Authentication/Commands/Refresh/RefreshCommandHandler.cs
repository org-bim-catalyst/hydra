using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.Refresh;

/// <summary>
/// Rotates the refresh token; reuse of an already-rotated (revoked) token revokes the
/// whole token family, per constitution &#167;8/research.md Topic 1.
/// </summary>
public sealed class RefreshCommandHandler(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IIdentityService identityService,
    IUnitOfWork unitOfWork,
    TokenIssuer tokenIssuer) : IRequestHandler<RefreshCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.Hash(request.RefreshToken);
        var existing = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        if (existing is null)
        {
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        if (!existing.IsActive)
        {
            // Reuse of an already-revoked/expired token: revoke the entire family.
            var family = await refreshTokenRepository.ListByFamilyAsync(existing.TokenFamilyId, cancellationToken);
            foreach (var token in family)
            {
                token.Revoke();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        existing.Revoke();

        var claims = await identityService.GetClaimsAsync(existing.UserId, cancellationToken);
        return await tokenIssuer.IssueAsync(existing.UserId, claims, existing.TokenFamilyId, cancellationToken);
    }
}
