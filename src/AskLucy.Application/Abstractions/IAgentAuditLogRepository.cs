using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="AgentAuditLog"/> (constitution §3 Repository rules) — deliberately its own top-level repository, not reachable via <see cref="IAgentExecutionRepository"/>, since it is intentionally not hard-linked to the execution it describes (data-model.md).</summary>
public interface IAgentAuditLogRepository
{
    void Add(AgentAuditLog log);

    Task<IReadOnlyList<AgentAuditLog>> ListByExecutionIdAsync(Guid agentExecutionId, CancellationToken cancellationToken = default);
}
