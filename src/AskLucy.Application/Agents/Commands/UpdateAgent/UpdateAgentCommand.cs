using AskLucy.Application.Agents;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.UpdateAgent;

public sealed record AgentToolInput(string ToolName, string? ConfigurationJson);

public sealed record AgentMemoryPolicyInput(bool AllowRead, bool AllowWriteProposals, string? PreApprovedCategoriesJson);

/// <summary>Draft-field edit (spec.md FR-001/FR-003) — never touches published version history. <see cref="Tools"/>/<see cref="KnowledgeBaseIds"/>/<see cref="MemoryPolicy"/> (User Story 2, FR-024/FR-029/FR-030) fully replace the prior draft configuration each call — null means "leave unchanged," an empty list means "clear."</summary>
public sealed record UpdateAgentCommand(
    Guid Id,
    string Name,
    string? Description,
    AgentType AgentType,
    AgentInstructionsDto Instructions,
    Guid? ModelProviderId,
    Guid? ModelId,
    AgentOutputFormat OutputFormat,
    AgentExecutionPolicyDto ExecutionPolicy,
    IReadOnlyList<AgentToolInput>? Tools = null,
    IReadOnlyList<Guid>? KnowledgeBaseIds = null,
    AgentMemoryPolicyInput? MemoryPolicy = null) : IRequest<AgentDetailDto>;
