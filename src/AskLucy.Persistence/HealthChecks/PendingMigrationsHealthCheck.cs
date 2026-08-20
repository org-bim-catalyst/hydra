using AskLucy.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AskLucy.Persistence.HealthChecks;

/// <summary>
/// specs/029-fix-chat-widget-bugs FR-012, research.md Decision 2 — catches schema drift (an
/// EF Core migration authored and committed but never applied to the deployed database)
/// before it manifests as a live-request failure, the root cause of the
/// <c>GET /api/v1/ai/voice/preferences</c> 500 this feature fixes. Uses EF Core's own
/// migration bookkeeping (<see cref="RelationalDatabaseFacadeExtensions.GetPendingMigrationsAsync"/>)
/// rather than a bespoke schema-diff mechanism — deliberately does not auto-apply migrations
/// (this project has no <c>Database.MigrateAsync()</c> call at startup by design; a readiness
/// check that only reports, never mutates, respects the controlled-deploy-gate requirement
/// for destructive schema changes, constitution §5).
/// </summary>
public sealed class PendingMigrationsHealthCheck(AskLucyDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            return HealthCheckResult.Healthy();
        }

        var data = new Dictionary<string, object> { ["pendingMigrations"] = pending };
        return HealthCheckResult.Unhealthy(
            $"{pending.Count} pending migration(s): {string.Join(", ", pending)}", data: data);
    }
}
