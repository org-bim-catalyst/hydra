using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents;

public sealed record AgentInstructionsDto(
    string? SystemInstructions,
    string? Objectives,
    string? Constraints,
    string? BehavioralRules,
    string? OutputRequirements,
    string? ToolUsageRules,
    string? SafetyRules)
{
    public static AgentInstructionsDto FromDomain(AgentInstructions instructions) => new(
        instructions.SystemInstructions, instructions.Objectives, instructions.Constraints,
        instructions.BehavioralRules, instructions.OutputRequirements, instructions.ToolUsageRules, instructions.SafetyRules);

    public AgentInstructions ToDomain() => new(SystemInstructions, Objectives, Constraints, BehavioralRules, OutputRequirements, ToolUsageRules, SafetyRules);
}

public sealed record AgentExecutionPolicyDto(
    int? MaxSteps,
    int? MaxExecutionDurationSeconds,
    int? MaxTokens,
    decimal? MaxCost,
    int? MaxToolCalls,
    int? MaxRetries)
{
    public static AgentExecutionPolicyDto FromDomain(AgentExecutionPolicy policy) => new(
        policy.MaxSteps, policy.MaxExecutionDurationSeconds, policy.MaxTokens, policy.MaxCost, policy.MaxToolCalls, policy.MaxRetries);

    public AgentExecutionPolicy ToDomain() => new(MaxSteps, MaxExecutionDurationSeconds, MaxTokens, MaxCost, MaxToolCalls, MaxRetries);
}

public sealed record AgentDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string AgentType,
    string Status,
    AgentInstructionsDto Instructions,
    Guid? ModelProviderId,
    Guid? ModelId,
    string OutputFormat,
    AgentExecutionPolicyDto ExecutionPolicy,
    int? PublishedVersionNumber,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<Guid> KnowledgeBaseIds,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc)
{
    public static AgentDetailDto Create(Agent agent) => new(
        agent.Id, agent.Name, agent.Description, agent.AgentType.ToString(), agent.Status.ToString(),
        AgentInstructionsDto.FromDomain(agent.Instructions), agent.ModelProviderId, agent.ModelId, agent.OutputFormat.ToString(),
        AgentExecutionPolicyDto.FromDomain(agent.ExecutionPolicy), agent.PublishedVersionNumber,
        agent.Tools.Select(t => t.ToolName).ToList(), agent.KnowledgeBases.Select(k => k.KnowledgeBaseId).ToList(),
        agent.CreatedAtUtc, agent.ModifiedAtUtc);
}

public sealed record AgentListItemDto(
    Guid Id, string Name, string? Description, string AgentType, string Status, int? PublishedVersionNumber, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc)
{
    public static AgentListItemDto Create(Agent agent) => new(
        agent.Id, agent.Name, agent.Description, agent.AgentType.ToString(), agent.Status.ToString(),
        agent.PublishedVersionNumber, agent.CreatedAtUtc, agent.ModifiedAtUtc);
}

