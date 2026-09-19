using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authentication;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// Seeding shared by the three US2 suites (specs/058-password-recovery). Reset links are minted
/// through the real repository rather than by driving the forgot-password endpoint: that endpoint
/// mails the token deliberately out of the test's reach, and the token's lifetime is a constructor
/// argument precisely so an expired one needs no waiting.
/// </summary>
internal static class PasswordResetTestHelper
{
    internal const string SeedPassword = "Seed-Password-1!";

    internal static async Task<string> SeedUserAsync(
        IServiceProvider services, string email, bool twoFactorEnabled = false)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TwoFactorEnabled = twoFactorEnabled,
            CreatedAtUtc = DateTime.UtcNow,
        };

        (await userManager.CreateAsync(user, SeedPassword)).Succeeded.Should().BeTrue();

        if (twoFactorEnabled)
        {
            // SignInManager only reports RequiresTwoFactor when the account actually has a usable
            // second-factor provider, so the flag alone is not enough to enrol a seeded account.
            await userManager.ResetAuthenticatorKeyAsync(user);
        }

        return user.Id;
    }

    /// <summary>An external-provider account: real, confirmed, and with no password at all (FR-014).</summary>
    internal static async Task<string> SeedPasswordlessUserAsync(IServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        (await userManager.HasPasswordAsync(user)).Should().BeFalse();

        return user.Id;
    }

    internal static async Task DeleteUsersAsync(IServiceProvider services, IEnumerable<string> userIds)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var userId in userIds)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    /// <summary>Issues a reset token and returns its plaintext — the value the emailed link carries.</summary>
    internal static async Task<string> IssueResetTokenAsync(
        IServiceProvider services, string userId, string email, TimeSpan? lifetime = null)
    {
        using var scope = services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var plaintext = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        tokens.Add(PasswordResetToken.IssueNew(
            userId, tokenService.Hash(plaintext), email, lifetime ?? TimeSpan.FromHours(1), "203.0.113.5"));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return plaintext;
    }
}
