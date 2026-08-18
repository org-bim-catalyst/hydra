using AskLucy.Application.Agents;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.CreateAgent;

/// <summary>Creates a new <see cref="Agent"/> in Draft status (spec.md FR-001-FR-006, contracts/agents-api.md).</summary>
public sealed record CreateAgentCommand(
    string Name,
    string? Description,
    AgentType AgentType,
    AgentInstructionsDto Instructions,
    Guid? ModelProviderId,
    Guid? ModelId,
    AgentOutputFormat OutputFormat,
    AgentExecutionPolicyDto ExecutionPolicy) : IRequest<AgentDetailDto>;
