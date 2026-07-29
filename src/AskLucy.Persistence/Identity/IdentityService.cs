using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
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
            CreatedAtUtc = DateTime.UtcNow,
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

    public async Task<IdentityOperationResult> ResolveExternalLoginAsync(
        string provider, string providerKey, string? email, bool emailVerified, string? linkToUserId,
        CancellationToken cancellationToken = default)
    {
        var existingLoginOwner = await userManager.FindByLoginAsync(provider, providerKey);

        if (linkToUserId is not null)
        {
            // FR-034: link an additional provider to the already-authenticated user. Refuse
            // if that exact (provider, providerKey) pair is already claimed by someone else —
            // never silently re-point an existing login at a different account.
            if (existingLoginOwner is not null && existingLoginOwner.Id != linkToUserId)
            {
                return new IdentityOperationResult(
                    IdentityResultStatus.Failed,
                    Errors: ["This account is already linked to a different Ask Lucy user."]);
            }

            var linkTarget = await userManager.FindByIdAsync(linkToUserId)
                ?? throw new InvalidOperationException($"User '{linkToUserId}' not found.");

            if (existingLoginOwner is null)
            {
                var linkResult = await userManager.AddLoginAsync(linkTarget, new UserLoginInfo(provider, providerKey, provider));
                if (!linkResult.Succeeded)
                {
                    return new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. linkResult.Errors.Select(e => e.Description)]);
                }
            }

            var linkedClaims = await GetClaimsAsync(linkTarget.Id, cancellationToken);
            return new IdentityOperationResult(IdentityResultStatus.Success, linkTarget.Id, linkedClaims);
        }

        // First-time / returning social sign-in (FR-010). `existingLoginOwner` is trusted here
        // only because the caller has already verified `provider`/`providerKey` against the
        // real OAuth provider — this method itself performs no such verification.
        var user = existingLoginOwner;

        if (user is null && emailVerified && email is not null)
        {
            // Only ever resolve/create by email when the *provider* asserts it's verified —
            // never a client-supplied, unverified email (the previous implementation's flaw).
            user = await userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
            }
            else
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. createResult.Errors.Select(e => e.Description)]);
                }

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

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        return result.Succeeded
            ? new IdentityOperationResult(IdentityResultStatus.Success, user.Id)
            : new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. result.Errors.Select(e => e.Description)]);
    }

    public async Task<bool> VerifyPasswordAsync(string userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        return await userManager.CheckPasswordAsync(user, password);
    }

    public async Task<bool> HasPasswordAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        return await userManager.HasPasswordAsync(user);
    }

    public async Task<string> GenerateChangeEmailTokenAsync(string userId, string newEmail, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        return await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
    }

    public async Task<IdentityOperationResult> ChangeEmailAsync(
        string userId, string newEmail, string token, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var result = await userManager.ChangeEmailAsync(user, newEmail, token);
        if (!result.Succeeded)
        {
            return new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. result.Errors.Select(e => e.Description)]);
        }

        // ChangeEmailAsync does not update UserName — our app treats email as the
        // username throughout (RegisterAsync sets UserName = email), so keep them in sync.
        await userManager.SetUserNameAsync(user, newEmail);

        return new IdentityOperationResult(IdentityResultStatus.Success, user.Id);
    }

    public async Task<IReadOnlyList<ExternalLoginDto>> GetExternalLoginsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var logins = await userManager.GetLoginsAsync(user);
        return [.. logins.Select(l => new ExternalLoginDto(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName ?? l.LoginProvider))];
    }

    public async Task<IdentityOperationResult> RemoveExternalLoginAsync(
        string userId, string provider, string providerKey, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var hasPassword = await userManager.HasPasswordAsync(user);
        var logins = await userManager.GetLoginsAsync(user);
        if (!hasPassword && logins.Count <= 1)
        {
            return new IdentityOperationResult(
                IdentityResultStatus.Failed,
                Errors: ["Cannot remove your only sign-in method. Set a password first."]);
        }

        var result = await userManager.RemoveLoginAsync(user, provider, providerKey);

        return result.Succeeded
            ? new IdentityOperationResult(IdentityResultStatus.Success, user.Id)
            : new IdentityOperationResult(IdentityResultStatus.Failed, Errors: [.. result.Errors.Select(e => e.Description)]);
    }

    public async Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await userManager.DeleteAsync(user);
    }

    public async Task SetLockoutAsync(string userId, bool locked, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
    }

    public async Task ChangeRoleAsync(string userId, string newRole, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentPrivilegedRoles = currentRoles.Where(PrivilegedRoleNames.All.Contains).ToArray();
        if (currentPrivilegedRoles.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, currentPrivilegedRoles);
        }

        // "Regular" is a sentinel meaning "no privileged role" — never a real AspNetRoles row
        // (data-model.md § Commands), so it is never passed to AddToRoleAsync.
        if (newRole != PrivilegedRoleNames.Regular)
        {
            await userManager.AddToRoleAsync(user, newRole);
        }
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? [] : [.. await userManager.GetRolesAsync(user)];
    }

    public async Task<int> CountActiveSuperUsersAsync(CancellationToken cancellationToken = default)
    {
        var superUsers = await userManager.GetUsersInRoleAsync(PrivilegedRoleNames.SuperUser);
        var now = DateTimeOffset.UtcNow;
        return superUsers.Count(u => u.LockoutEnd is null || u.LockoutEnd <= now);
    }
}
