using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="Agent"/> (constitution §3 Repository rules).</summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Bypasses owner scoping — used only where the caller has already authorized access some other way (e.g. the Agent Runtime resolving the agent an execution belongs to, not the HTTP caller directly).</summary>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Agent agent);

    /// <summary>Cursor-based (keyset) listing over the caller's own agents (contracts/agents-api.md <c>ListAgentsQuery</c>).</summary>
    Task<(IReadOnlyList<Agent> Items, string? NextCursor)> ListByOwnerAsync(
        string ownerId, AgentStatus? status, AgentType? agentType, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    Task<AgentVersion?> GetVersionAsync(Guid agentId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>Looked up by the version's own id — used where a caller (e.g. <c>AgentExecution.AgentVersionId</c>) only has that, not the version number.</summary>
    Task<AgentVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentVersion>> ListVersionsAsync(Guid agentId, CancellationToken cancellationToken = default);
}
