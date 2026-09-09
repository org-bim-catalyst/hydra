namespace AskLucy.Application.Conversations.SystemAgents;

/// <summary>
/// Tally of one provisioning pass over <see cref="SystemAgentDefinitions.All"/>
/// (contracts/system-agent-provisioning.md §2).
/// </summary>
/// <param name="Deferred">
/// True when the pass performed no writes at all because the database was unreachable or had
/// pending migrations — never thrown, so the host still starts and the next startup retries.
/// </param>
public sealed record SystemAgentProvisioningResult(int Created, int Upgraded, int Unchanged, bool Deferred)
{
    public static readonly SystemAgentProvisioningResult DeferredResult = new(0, 0, 0, Deferred: true);
}

/// <summary>
/// Upserts <see cref="SystemAgentDefinitions.All"/> into the agent catalog (specs/045 FR-033,
/// FR-035, SC-011). Driven by <c>SystemAgentProvisioningHostedService</c> at startup.
/// </summary>
public interface ISystemAgentProvisioner
{
    Task<SystemAgentProvisioningResult> ProvisionAsync(CancellationToken cancellationToken);
}
