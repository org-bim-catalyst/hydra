using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ConfirmEmail;

public sealed class ConfirmEmailCommandHandler(IIdentityService identityService)
    : IRequestHandler<ConfirmEmailCommand, bool>
{
    public Task<bool> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken) =>
        identityService.ConfirmEmailAsync(request.UserId, request.Token, cancellationToken);
}
