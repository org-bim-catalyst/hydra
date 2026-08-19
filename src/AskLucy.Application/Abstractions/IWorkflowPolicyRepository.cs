using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="WorkflowPolicy"/> and its sibling <see cref="WorkflowUserExecutionLimit"/> (data-model.md's "Aggregate: WorkflowPolicy" section covers both, constitution §3 Repository rules).</summary>
public interface IWorkflowPolicyRepository
{
    Task<WorkflowPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(WorkflowPolicy policy);

    /// <summary>Enabled policies targeting a given node type and/or underlying tool — evaluated by <c>WorkflowPolicyEvaluator</c> against an intended node action (research.md Decision 5).</summary>
    Task<IReadOnlyList<WorkflowPolicy>> ListEnabledForNodeAsync(WorkflowNodeType nodeType, string? underlyingToolName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowPolicy>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<WorkflowUserExecutionLimit?> GetUserExecutionLimitAsync(string userId, CancellationToken cancellationToken = default);

    void AddUserExecutionLimit(WorkflowUserExecutionLimit limit);
}
