using MediatR;

namespace AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;

/// <summary>Pauses a running execution (spec.md User Story 6) — observed by the orchestrator at the next node boundary (FR-048, SC-007: ≤5s).</summary>
public sealed record PauseWorkflowExecutionCommand(Guid ExecutionId) : IRequest;
