using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.Logout;

public sealed class LogoutCommandHandler(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    ISessionRevocationCache sessionRevocationCache,
    IUnitOfWork unitOfWork) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.Hash(request.RefreshToken);
        var existing = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        if (existing is not null)
        {
            existing.Revoke();
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Revoking the last live token in the family ends the session, so the access token
            // this browser still holds must stop working now rather than when it expires.
            sessionRevocationCache.Evict(existing.TokenFamilyId);
        }
    }
}
