using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.LoginTwoFactor;

public sealed class LoginTwoFactorCommandHandler(IIdentityService identityService, TokenIssuer tokenIssuer)
    : IRequestHandler<LoginTwoFactorCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.ValidateTwoFactorCodeAsync(
            request.UserId, request.Code, request.IsRecoveryCode, cancellationToken);

        if (result.Status != IdentityResultStatus.Success || result.UserId is null)
        {
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        return await tokenIssuer.IssueAsync(result.UserId, result.Claims ?? [], rotatingFamilyId: null, cancellationToken);
    }
}
