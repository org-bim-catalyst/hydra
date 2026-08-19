using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows;

public sealed record WorkflowPolicyDto(
    Guid Id, string Name, string? Description, string? WorkflowNodeType, string? UnderlyingToolName, string? ConditionsJson,
    string CreatedByUserId, bool IsEnabled, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc)
{
    public static WorkflowPolicyDto Create(WorkflowPolicy policy) => new(
        policy.Id, policy.Name, policy.Description, policy.WorkflowNodeType?.ToString(), policy.UnderlyingToolName, policy.ConditionsJson,
        policy.CreatedByUserId, policy.IsEnabled, policy.CreatedAtUtc, policy.ModifiedAtUtc);
}
