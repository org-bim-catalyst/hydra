using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="AgentPolicy"/> and its sibling <see cref="AgentUserExecutionLimit"/> (data-model.md's "Aggregate: AgentPolicy" section covers both, constitution §3 Repository rules).</summary>
public interface IAgentPolicyRepository
{
    Task<AgentPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(AgentPolicy policy);

    /// <summary>Enabled policies targeting a given tool — evaluated by <c>AgentPolicyEvaluator</c> against an intended tool call (research.md Decision 1).</summary>
    Task<IReadOnlyList<AgentPolicy>> ListEnabledByToolNameAsync(string toolName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentPolicy>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<AgentUserExecutionLimit?> GetUserExecutionLimitAsync(string userId, CancellationToken cancellationToken = default);

    void AddUserExecutionLimit(AgentUserExecutionLimit limit);
}
