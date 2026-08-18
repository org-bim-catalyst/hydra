using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Authorization;

/// <summary>Centralizes the "does this agent belong to this caller" check (spec.md FR-048), mirrors <c>PromptOwnershipGuard</c>/<c>MemoryOwnershipGuard</c>/<c>KnowledgeBaseOwnershipGuard</c>. Denial looks like not-found — a request naming an agent the caller doesn't own returns 404, never 403, avoiding existence disclosure.</summary>
public static class AgentOwnershipGuard
{
    public static Agent EnsureOwnedBy(Agent? agent, string userId)
    {
        if (agent is null || agent.OwnerId != userId)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        return agent;
    }
}
