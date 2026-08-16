using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>
/// An administrator- or owner-defined rule that pre-approves specific node actions under defined
/// conditions (FR-035, data-model.md, research.md Decision 5) — mirrors <c>AgentPolicy</c>'s shape.
/// A second, workflow-specific entity rather than reusing <c>AgentPolicy</c> directly: an
/// <c>AgentPolicy</c> row is scoped to a single <c>AgentToolCall</c>, and giving it a second,
/// mutually-exclusive optional FK to a <see cref="WorkflowExecutionNode"/> would be exactly the
/// ambiguous-ownership shape this codebase avoids elsewhere.
/// </summary>
public sealed class WorkflowPolicy : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Scopes the policy to a node type (FR-035's "Require Approval For Specific Node Types"); null = applies by risk level instead.</summary>
    public WorkflowNodeType? WorkflowNodeType { get; private set; }

    /// <summary>For capability-wrapping node types, matches the underlying <c>IAgentTool.Name</c> (same shape <c>AgentPolicy.ToolName</c> already uses).</summary>
    public string? UnderlyingToolName { get; private set; }

    public string? ConditionsJson { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; } = true;

    private WorkflowPolicy()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowPolicy Create(string name, string? description, WorkflowNodeType? workflowNodeType, string? underlyingToolName, string? conditionsJson, string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A policy name is required.");
        }

        if (workflowNodeType is null && string.IsNullOrWhiteSpace(underlyingToolName))
        {
            throw new DomainRuleViolationException("A policy must target either a node type or an underlying tool.");
        }

        return new WorkflowPolicy
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            WorkflowNodeType = workflowNodeType,
            UnderlyingToolName = underlyingToolName,
            ConditionsJson = conditionsJson,
            CreatedByUserId = createdByUserId,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = createdByUserId,
        };
    }

    public void Update(string name, string? description, string? conditionsJson, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A policy name is required.");
        }

        Name = name.Trim();
        Description = description;
        ConditionsJson = conditionsJson;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetEnabled(bool isEnabled, string actor)
    {
        IsEnabled = isEnabled;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
