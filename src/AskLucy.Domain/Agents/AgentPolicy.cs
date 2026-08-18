using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// Administrator-managed auto-approval rule (spec.md FR-025/FR-026, data-model.md, research.md
/// Decision 1). <see cref="OrganizationId"/> is reserved for a future multi-tenancy feature and
/// is always <c>null</c> this release; policy management is instead gated by the
/// Administrator/Super User role (the same <c>AdministratorOrSuperUser</c> authorization policy
/// used elsewhere in the platform).
/// </summary>
public sealed class AgentPolicy : BaseEntity
{
    public Guid? OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string ToolName { get; private set; } = string.Empty;

    public string? ConditionsJson { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; } = true;

    private AgentPolicy()
    {
        // Required by EF Core materialization.
    }

    public static AgentPolicy Create(string name, string? description, string toolName, string? conditionsJson, string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A policy name is required.");
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new DomainRuleViolationException("A policy must target a tool.");
        }

        return new AgentPolicy
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            ToolName = toolName,
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
