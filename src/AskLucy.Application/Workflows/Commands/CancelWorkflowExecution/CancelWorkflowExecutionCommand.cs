using MediatR;

namespace AskLucy.Application.Workflows.Commands.CancelWorkflowExecution;

/// <summary>Cancels a non-terminal execution (spec.md User Story 6) — observed by the orchestrator at the next node boundary (FR-048, SC-007: ≤5s).</summary>
public sealed record CancelWorkflowExecutionCommand(Guid ExecutionId) : IRequest;
