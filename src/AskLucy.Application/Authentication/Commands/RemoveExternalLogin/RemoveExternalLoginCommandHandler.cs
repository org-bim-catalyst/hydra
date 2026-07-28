using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.RemoveExternalLogin;

public sealed class RemoveExternalLoginCommandHandler(IIdentityService identityService)
    : IRequestHandler<RemoveExternalLoginCommand, IdentityOperationResult>
{
    public Task<IdentityOperationResult> Handle(RemoveExternalLoginCommand request, CancellationToken cancellationToken) =>
        identityService.RemoveExternalLoginAsync(request.UserId, request.Provider, request.ProviderKey, cancellationToken);
}
