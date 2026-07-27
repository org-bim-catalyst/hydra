using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.Login;

public sealed class LoginCommandHandler(IIdentityService identityService, TokenIssuer tokenIssuer)
    : IRequestHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken);

        return result.Status switch
        {
            IdentityResultStatus.RequiresTwoFactor => new AuthResult(AuthOutcome.RequiresTwoFactor, result.UserId),
            IdentityResultStatus.EmailNotConfirmed => new AuthResult(AuthOutcome.EmailNotConfirmed, result.UserId),
            IdentityResultStatus.LockedOut => new AuthResult(AuthOutcome.LockedOut, result.UserId),
            IdentityResultStatus.Success when result.UserId is not null =>
                await tokenIssuer.IssueAsync(result.UserId, result.Claims ?? [], rotatingFamilyId: null, cancellationToken),
            _ => new AuthResult(AuthOutcome.InvalidCredentials),
        };
    }
}
