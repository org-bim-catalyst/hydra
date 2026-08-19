using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionEvents;

/// <summary>Reconciliation fallback for a client that missed a live <c>WorkflowExecutionHub</c> push (contracts/workflow-execution-events.md) — every event strictly after <paramref name="Since"/>, oldest first.</summary>
public sealed record GetWorkflowExecutionEventsQuery(Guid ExecutionId, DateTime? Since) : IRequest<IReadOnlyList<WorkflowExecutionEventDto>>;
