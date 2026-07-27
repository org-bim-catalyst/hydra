using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class DisableTwoFactorCommandHandler(IIdentityService identityService)
    : IRequestHandler<DisableTwoFactorCommand>
{
    public Task Handle(DisableTwoFactorCommand request, CancellationToken cancellationToken) =>
        identityService.DisableTwoFactorAsync(request.UserId, cancellationToken);
}
