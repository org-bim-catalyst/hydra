using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Queries.ValidatePasswordResetToken;

/// <summary>
/// Mirrors the lifecycle checks <c>ResetPasswordCommandHandler</c> performs before it redeems a
/// token, without consuming it. Kept deliberately in step with that handler: if one gains a
/// rejection cause the other must too, or the page would invite a password the redeem step then
/// refuses.
/// </summary>
public sealed class ValidatePasswordResetTokenQueryHandler(
    IPasswordResetTokenRepository resetTokenRepository,
    IIdentityService identityService,
    ITokenService tokenService) : IRequestHandler<ValidatePasswordResetTokenQuery, bool>
{
    public async Task<bool> Handle(ValidatePasswordResetTokenQuery request, CancellationToken cancellationToken)
    {
        var token = await resetTokenRepository.FindByHashAsync(tokenService.Hash(request.Token), cancellationToken);
        if (token is null || !string.Equals(token.UserId, request.UserId, StringComparison.Ordinal))
        {
            return false;
        }

        var eligibility = await identityService.GetPasswordResetEligibilityAsync(token.UserId, cancellationToken);
        return eligibility is not null && token.CanRedeemFor(eligibility.Email);
    }
}
