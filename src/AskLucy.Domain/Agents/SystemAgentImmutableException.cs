namespace AskLucy.Domain.Agents;

/// <summary>
/// Thrown by <see cref="Application.Agents.Authorization.SystemAgentMutationGuard"/> when a user
/// mutation path targets a platform-provisioned agent (specs/045-conversational-agent-runtime
/// FR-034, contracts/system-agent-provisioning.md §3) — mapped to <c>403 Forbidden</c>,
/// <c>system-agent-immutable</c>. Reads stay open; only the five mutation commands
/// (update/publish/archive/restore/delete) reject a system agent.
/// </summary>
public sealed class SystemAgentImmutableException(Guid agentId)
    : Exception("This agent is provisioned by Ask Lucy and cannot be modified.")
{
    public Guid AgentId { get; } = agentId;
}
