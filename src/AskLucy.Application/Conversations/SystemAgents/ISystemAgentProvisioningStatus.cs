namespace AskLucy.Application.Conversations.SystemAgents;

/// <summary>
/// The last provisioning pass's outcome, shared between
/// <c>SystemAgentProvisioningHostedService</c> (the writer) and a <c>/health/ready</c> health
/// check (the reader) — a singleton, since a health check request and the hosted service run in
/// different DI scopes but must observe the same in-process fact.
/// <para>
/// <see cref="IsDeferred"/> starts <see langword="false"/> so a health probe hitting the endpoint
/// before the hosted service has run even once reports healthy rather than falsely degraded —
/// provisioning not having run yet is not the same fact as provisioning having failed.
/// </para>
/// </summary>
public interface ISystemAgentProvisioningStatus
{
    bool IsDeferred { get; }

    void RecordResult(SystemAgentProvisioningResult result);
}

/// <summary>In-memory, process-lifetime implementation — this status is a runtime fact, not something a restart should remember (contracts/system-agent-provisioning.md §2's "the next startup retries").</summary>
public sealed class SystemAgentProvisioningStatus : ISystemAgentProvisioningStatus
{
    private volatile bool _isDeferred;

    public bool IsDeferred => _isDeferred;

    public void RecordResult(SystemAgentProvisioningResult result) => _isDeferred = result.Deferred;
}
