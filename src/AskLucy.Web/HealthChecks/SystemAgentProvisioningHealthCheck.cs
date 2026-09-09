using AskLucy.Application.Conversations.SystemAgents;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AskLucy.Web.HealthChecks;

/// <summary>
/// specs/045 T105 — surfaces a deferred system-agent provisioning pass as degraded on
/// <c>/health/ready</c> (contracts/system-agent-provisioning.md §2's "the gap is visible rather
/// than silent," constitution §2.VIII). Degraded, not unhealthy: a deferred pass means the
/// orchestrator/sub-agents may be running an older definition or (on a brand-new database) may
/// not exist yet, but the application itself still serves ordinary requests — this is not the
/// same failure a database outage represents.
/// </summary>
public sealed class SystemAgentProvisioningHealthCheck(ISystemAgentProvisioningStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(status.IsDeferred
            ? HealthCheckResult.Degraded("System agent provisioning was deferred (pending migrations or an unreachable database); it will retry on the next startup.")
            : HealthCheckResult.Healthy());
}
