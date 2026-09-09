using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.AspNetCore.Identity;

namespace AskLucy.Persistence.Identity;

/// <summary>
/// specs/047 — <see cref="Agent.SystemOwnerId"/> ("system") is used as <c>Agents.OwnerId</c> by
/// <c>SystemAgentProvisioner</c>, but that column carries a real foreign key to
/// <see cref="ApplicationUser"/> (<c>AgentConfiguration.cs</c>) — there was previously no row
/// backing it, so every system-agent creation attempt failed its <c>SaveChanges</c> with an FK
/// violation, silently caught and logged by <c>SystemAgentProvisioner</c>'s own per-definition
/// error handling (constitution §2.VIII: the failure was surfaced to logs, never silently
/// discarded — but nothing had ever pointed a reader at this specific cause before). This is not
/// a real, sign-in-capable account: no password is ever set, and nothing authenticates as it.
/// </summary>
public sealed class SystemAccountProvisioner(UserManager<ApplicationUser> userManager) : ISystemAccountProvisioner
{
    private const string SystemAccountEmail = "system@asklucy.internal";

    public async Task EnsureSystemAccountExistsAsync(CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByIdAsync(Agent.SystemOwnerId) is not null)
        {
            return;
        }

        var systemUser = new ApplicationUser
        {
            Id = Agent.SystemOwnerId,
            UserName = SystemAccountEmail,
            Email = SystemAccountEmail,
            EmailConfirmed = true,
            LockoutEnabled = false,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(systemUser);
        if (!result.Succeeded && await userManager.FindByIdAsync(Agent.SystemOwnerId) is null)
        {
            // Not a race another instance already won (that row would exist by now) — a genuine
            // creation failure. Thrown, not swallowed: the caller's own defer-and-retry-next-
            // startup handling (constitution §2.VIII) is what turns this into a safe outcome.
            throw new InvalidOperationException(
                $"Failed to provision the system account: {string.Join(' ', result.Errors.Select(e => e.Description))}");
        }
    }
}
