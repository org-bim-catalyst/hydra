using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>Per-user override of the system-wide concurrent-execution cap (FR-069/FR-070, data-model.md, research.md Decision 11) — field-for-field mirror of <c>AgentUserExecutionLimit</c>; tracked independently of a user's agent-execution cap.</summary>
public sealed class WorkflowUserExecutionLimit : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public int MaxConcurrentExecutions { get; private set; }

    public string SetByUserId { get; private set; } = string.Empty;

    private WorkflowUserExecutionLimit()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowUserExecutionLimit Create(string userId, int maxConcurrentExecutions, string setByUserId)
    {
        if (maxConcurrentExecutions < 1)
        {
            throw new DomainRuleViolationException("Maximum concurrent executions must be at least 1.");
        }

        return new WorkflowUserExecutionLimit
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MaxConcurrentExecutions = maxConcurrentExecutions,
            SetByUserId = setByUserId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = setByUserId,
        };
    }

    public void Update(int maxConcurrentExecutions, string actor)
    {
        if (maxConcurrentExecutions < 1)
        {
            throw new DomainRuleViolationException("Maximum concurrent executions must be at least 1.");
        }

        MaxConcurrentExecutions = maxConcurrentExecutions;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
