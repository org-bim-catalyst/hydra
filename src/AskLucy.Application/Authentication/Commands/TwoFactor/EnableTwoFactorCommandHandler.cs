using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class EnableTwoFactorCommandHandler(IIdentityService identityService)
    : IRequestHandler<EnableTwoFactorCommand, string>
{
    public Task<string> Handle(EnableTwoFactorCommand request, CancellationToken cancellationToken) =>
        identityService.EnableTwoFactorAsync(request.UserId, cancellationToken);
}
