using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

public sealed class ConfirmEmailChangeCommandHandler(IIdentityService identityService)
    : IRequestHandler<ConfirmEmailChangeCommand, IdentityOperationResult>
{
    public Task<IdentityOperationResult> Handle(ConfirmEmailChangeCommand request, CancellationToken cancellationToken) =>
        identityService.ChangeEmailAsync(request.UserId, request.NewEmail, request.Token, cancellationToken);
}
