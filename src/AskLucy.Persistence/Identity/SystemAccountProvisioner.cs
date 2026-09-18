using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
public sealed class SystemAccountProvisioner(UserManager<ApplicationUser> userManager, AskLucyDbContext dbContext) : ISystemAccountProvisioner
{
    private const string SystemAccountEmail = "system@asklucy.internal";

    public async Task EnsureSystemAccountExistsAsync(CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByIdAsync(Agent.SystemOwnerId) is not null)
        {
            return;
        }

        // The row may already exist but be soft-deleted (constitution §5's IsDeleted convention,
        // ApplicationUserConfiguration's global query filter) — the active-only check above can
        // never see it, so without this an environment where the account was ever soft-deleted
        // would fail every single startup, deterministically, with the same duplicate-key
        // violation the try/catch below exists to handle for a genuine concurrent-creation race:
        // the primary key still physically exists, so every fresh INSERT attempt collides with
        // it forever, not just once. Restoring it here — rather than assuming "not found (active)"
        // means "never created" — is what makes this idempotent regardless of how the row got
        // into that state.
        var softDeleted = await dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == Agent.SystemOwnerId && u.IsDeleted, cancellationToken);
        if (softDeleted is not null)
        {
            softDeleted.IsDeleted = false;
            softDeleted.DeletedAtUtc = null;
            softDeleted.DeletedBy = null;
            await dbContext.SaveChangesAsync(cancellationToken);
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

        // specs/057-site-analysis-agent — a second startup hosted service
        // (SystemWorkflowProvisioningHostedService) now calls this concurrently alongside
        // SystemAgentProvisioningHostedService, both racing the same "does the row exist yet"
        // check above on a fresh database. ASP.NET Core Identity's default UserStore does not
        // translate a duplicate-key SaveChanges failure into a graceful IdentityResult.Failed —
        // it lets the raw DbUpdateException propagate through CreateAsync. Caught here, not
        // swallowed: re-checked against the row a concurrent winner just committed, and only
        // rethrown if the row genuinely still doesn't exist.
        try
        {
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
        catch (DbUpdateException)
        {
            // A concurrent caller may have won the race and already committed the row, in which
            // case the outcome this method promises (the account exists) already holds and this
            // is not a failure. Re-checked, not assumed — a genuinely different failure rethrows.
            if (await userManager.FindByIdAsync(Agent.SystemOwnerId) is null)
            {
                throw;
            }
        }
    }
}
