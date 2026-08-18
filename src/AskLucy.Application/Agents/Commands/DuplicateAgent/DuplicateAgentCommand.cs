using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.DuplicateAgent;

/// <summary>Copies the current draft only, never version/execution history (spec.md User Story 6, Acceptance Scenario 4).</summary>
public sealed record DuplicateAgentCommand(Guid Id) : IRequest<AgentDetailDto>;
