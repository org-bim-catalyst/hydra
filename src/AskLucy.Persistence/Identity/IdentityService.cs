using System.Security.Claims;
using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace AskLucy.Persistence.Identity;

/// <summary>
/// Implements <see cref="IIdentityService"/> using ASP.NET Core Identity's
/// <see cref="UserManager{TUser}"/>/<see cref="SignInManager{TUser}"/>. Lives in
/// Persistence (not Infrastructure) because <see cref="ApplicationUser"/> and the
/// Identity store already live here — see research.md Topic 1.
/// </summary>
public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IIdentityService
{
    public async Task<IdentityOperationResult> RegisterAsync(
        string email, string password, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
        };

        var result = await userManager.CreateAsync(user, password);

        return result.Succeeded
            ? new IdentityOperationResult(IdentityResultStatus.Success, user.Id)
            : new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. result.Errors.Select(e => e.Description)]);
    }

    public async Task<IdentityOperationResult> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return new IdentityOperationResult(IdentityResultStatus.InvalidCredentials);
        }

        if (!user.EmailConfirmed)
        {
            return new IdentityOperationResult(IdentityResultStatus.EmailNotConfirmed, user.Id);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            return new IdentityOperationResult(IdentityResultStatus.RequiresTwoFactor, user.Id);
        }

        if (result.IsLockedOut)
        {
            return new IdentityOperationResult(IdentityResultStatus.LockedOut, user.Id);
        }

        if (!result.Succeeded)
        {
            return new IdentityOperationResult(IdentityResultStatus.InvalidCredentials);
        }

        var claims = await GetClaimsAsync(user.Id, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, user.Id, claims);
    }

    public async Task<IdentityOperationResult> ValidateTwoFactorCodeAsync(
        string userId, string code, bool isRecoveryCode, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return new IdentityOperationResult(IdentityResultStatus.InvalidCredentials);
        }

        var result = isRecoveryCode
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(code)
            : await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);

        if (!result.Succeeded)
        {
            return new IdentityOperationResult(IdentityResultStatus.InvalidCredentials, user.Id);
        }

        var claims = await GetClaimsAsync(user.Id, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, user.Id, claims);
    }

    public async Task<IdentityOperationResult> ValidateExternalLoginAsync(
        string provider, string providerKey, string? email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByLoginAsync(provider, providerKey);

        if (user is null && email is not null)
        {
            user = await userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
            }
        }

        if (user is null)
        {
            return new IdentityOperationResult(IdentityResultStatus.InvalidCredentials);
        }

        var claims = await GetClaimsAsync(user.Id, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, user.Id, claims);
    }

    public async Task<IReadOnlyList<Claim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var roles = await userManager.GetRolesAsync(user);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];

        return claims;
    }

    public async Task<string> EnableTwoFactorAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Failed to generate an authenticator key.");

        await userManager.SetTwoFactorEnabledAsync(user, true);
        return key;
    }

    public async Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, number: 10);
        return [.. codes ?? []];
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        return await userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }
}
