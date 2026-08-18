using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Authorization;

/// <summary>Centralizes the "does this execution belong to this caller" check (spec.md FR-048/SC-010), mirrors <see cref="AgentOwnershipGuard"/>. Denial looks like not-found — a request naming another user's execution returns 404, never 403.</summary>
public static class AgentExecutionOwnershipGuard
{
    public static AgentExecution EnsureOwnedBy(AgentExecution? execution, string userId)
    {
        if (execution is null || execution.RunByUserId != userId)
        {
            throw new KeyNotFoundException("Agent execution not found.");
        }

        return execution;
    }
}
