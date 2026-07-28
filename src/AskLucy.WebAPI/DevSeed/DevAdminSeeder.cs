using AskLucy.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.WebAPI.DevSeed;

/// <summary>
/// Dev-only convenience: ensures the "Administrator"/"Super User" roles exist and, if no
/// Administrator account exists yet, creates one from configuration. Never hardcodes a
/// credential in source — ADR-0001 explicitly rejected the legacy pattern of a plaintext
/// seed-admin password baked into EF migrations. Runs only in Development (see Program.cs);
/// does nothing if <c>SeedAdmin:Email</c>/<c>SeedAdmin:Password</c> aren't configured, and
/// never touches anything once at least one Administrator/Super User already exists.
/// </summary>
public static class DevAdminSeeder
{
    private static readonly string[] AdminRoles = ["Administrator", "Super User"];

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        foreach (var role in AdminRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminsExist = false;
        foreach (var role in AdminRoles)
        {
            if ((await userManager.GetUsersInRoleAsync(role)).Count > 0)
            {
                adminsExist = true;
                break;
            }
        }

        if (adminsExist)
        {
            return;
        }

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            DevSeedLog.NoSeedAdminConfigured(logger);
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                DevSeedLog.SeedAdminCreationFailed(logger, string.Join(' ', createResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        await userManager.AddToRoleAsync(user, "Administrator");
        DevSeedLog.SeedAdminReady(logger, email);
    }
}

internal static partial class DevSeedLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "No SeedAdmin:Email/SeedAdmin:Password configured — skipping dev admin seed. Set them via `dotnet user-secrets` to get an initial Administrator account.")]
    public static partial void NoSeedAdminConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create the dev seed admin account: {Errors}")]
    public static partial void SeedAdminCreationFailed(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dev seed admin ready: {Email}")]
    public static partial void SeedAdminReady(ILogger logger, string email);
}
