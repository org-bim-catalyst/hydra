using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>Per-user override of the system-wide concurrent-execution cap (spec.md FR-042/FR-043, data-model.md, research.md Decision 2 — no <c>SubscriptionTier</c> concept exists yet, so this is per-user only).</summary>
public sealed class AgentUserExecutionLimit : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public int MaxConcurrentExecutions { get; private set; }

    public string SetByUserId { get; private set; } = string.Empty;

    private AgentUserExecutionLimit()
    {
        // Required by EF Core materialization.
    }

    public static AgentUserExecutionLimit Create(string userId, int maxConcurrentExecutions, string setByUserId)
    {
        if (maxConcurrentExecutions < 1)
        {
            throw new DomainRuleViolationException("Maximum concurrent executions must be at least 1.");
        }

        return new AgentUserExecutionLimit
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
