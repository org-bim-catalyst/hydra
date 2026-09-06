using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetSession;

/// <summary>
/// Read-only session check backing the frontend's route guards: validates the refresh-token
/// cookie is still active WITHOUT rotating it, so it never competes with (or interferes with)
/// the reuse-detection/rotation logic in <see cref="Commands.Refresh.RefreshCommandHandler"/>.
/// </summary>
public sealed class GetSessionQueryHandler(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IIdentityService identityService) : IRequestHandler<GetSessionQuery, SessionResult>
{
    public async Task<SessionResult> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        var hash = tokenService.Hash(request.RefreshToken);
        var existing = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        if (existing is null || !existing.IsActive)
        {
            return SessionResult.Anonymous;
        }

        var roles = await identityService.GetRolesAsync(existing.UserId, cancellationToken);
        return new SessionResult(true, existing.UserId, roles);
    }
}
