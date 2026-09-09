using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Authorization;

/// <summary>
/// Centralizes the "a platform-provisioned agent cannot be mutated by a user" check
/// (specs/045-conversational-agent-runtime FR-034, contracts/system-agent-provisioning.md §3).
/// Called from every one of the five agent mutation commands — update, publish, archive, restore,
/// delete — right after <see cref="AgentOwnershipGuard.EnsureOwnedBy"/>, so a future command
/// cannot forget the check by construction rather than by convention.
/// <para>
/// <b>Belt and suspenders, not the only gate.</b> Every system agent's <c>OwnerId</c> is
/// <see cref="Agent.SystemOwnerId"/>, so <see cref="AgentOwnershipGuard.EnsureOwnedBy"/> already
/// 404s any ordinary caller's attempt to reach one through the owner-scoped lookup these five
/// handlers use today. This guard is what keeps that true independent of how a caller reached the
/// entity — e.g. an administrative mutation path added later that looks agents up by id without
/// owner scoping — rather than leaning on ownership scoping as the only thing standing between a
/// user and a system agent's definition.
/// </para>
/// </summary>
public static class SystemAgentMutationGuard
{
    public static Agent EnsureMutable(Agent agent)
    {
        if (agent.IsSystemOwned)
        {
            throw new SystemAgentImmutableException(agent.Id);
        }

        return agent;
    }
}
