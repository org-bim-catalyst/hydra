using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

public sealed class CompleteExternalLoginCommandHandler(
    IExternalLoginCodeStore codeStore, IIdentityService identityService, TokenIssuer tokenIssuer)
    : IRequestHandler<CompleteExternalLoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(CompleteExternalLoginCommand request, CancellationToken cancellationToken)
    {
        var userId = codeStore.TryConsume(request.Code);
        if (userId is null)
        {
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        var claims = await identityService.GetClaimsAsync(userId, cancellationToken);
        return await tokenIssuer.IssueAsync(userId, claims, rotatingFamilyId: null, cancellationToken);
    }
}
