using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(IIdentityService identityService)
    : IRequestHandler<ChangePasswordCommand, IdentityOperationResult>
{
    public Task<IdentityOperationResult> Handle(ChangePasswordCommand request, CancellationToken cancellationToken) =>
        identityService.ChangePasswordAsync(request.UserId, request.CurrentPassword, request.NewPassword, cancellationToken);
}
