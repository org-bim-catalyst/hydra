using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.DeleteMyAccount;

/// <summary>Requires the current password as re-confirmation before an irreversible account deletion.</summary>
public sealed class DeleteMyAccountCommandHandler(IIdentityService identityService, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteMyAccountCommand, IdentityOperationResult>
{
    public async Task<IdentityOperationResult> Handle(DeleteMyAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var passwordValid = await identityService.VerifyPasswordAsync(userId, request.Password, cancellationToken);
        if (!passwordValid)
        {
            return new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["Incorrect password."]);
        }

        await identityService.DeleteAsync(userId, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, userId);
    }
}
