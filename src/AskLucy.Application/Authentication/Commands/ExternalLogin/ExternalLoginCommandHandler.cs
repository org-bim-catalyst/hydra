using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

/// <summary>Preserves FR-010: Google/Facebook social sign-in, now behind the JWT flow.</summary>
public sealed class ExternalLoginCommandHandler(IIdentityService identityService, TokenIssuer tokenIssuer)
    : IRequestHandler<ExternalLoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(ExternalLoginCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.ValidateExternalLoginAsync(
            request.Provider, request.ProviderKey, request.Email, cancellationToken);

        if (result.Status != IdentityResultStatus.Success || result.UserId is null)
        {
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        return await tokenIssuer.IssueAsync(result.UserId, result.Claims ?? [], rotatingFamilyId: null, cancellationToken);
    }
}
