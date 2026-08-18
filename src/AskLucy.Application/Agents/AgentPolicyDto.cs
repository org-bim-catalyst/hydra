using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents;

public sealed record AgentPolicyDto(
    Guid Id, string Name, string? Description, string ToolName, string? ConditionsJson,
    string CreatedByUserId, bool IsEnabled, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc)
{
    public static AgentPolicyDto Create(AgentPolicy policy) => new(
        policy.Id, policy.Name, policy.Description, policy.ToolName, policy.ConditionsJson,
        policy.CreatedByUserId, policy.IsEnabled, policy.CreatedAtUtc, policy.ModifiedAtUtc);
}
