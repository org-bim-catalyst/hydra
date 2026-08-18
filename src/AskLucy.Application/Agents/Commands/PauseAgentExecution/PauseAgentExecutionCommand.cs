using MediatR;

namespace AskLucy.Application.Agents.Commands.PauseAgentExecution;

/// <summary>Pauses a running execution (spec.md User Story 4) — observed by the orchestrator at the next step boundary (FR-017, SC-009: ≤5s).</summary>
public sealed record PauseAgentExecutionCommand(Guid ExecutionId) : IRequest;
