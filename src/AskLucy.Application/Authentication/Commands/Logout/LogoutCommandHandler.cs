using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.Logout;

public sealed class LogoutCommandHandler(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
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
        }
    }
}
