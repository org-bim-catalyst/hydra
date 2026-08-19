using MediatR;

namespace AskLucy.Application.Workflows.Commands.RetryWorkflowExecutionNode;

/// <summary>Manual retry of a `Failed` node (spec.md User Story 7) — resets the node and the execution, then re-enqueues the runner to resume from that same row (research.md Decision 13's idempotency-key check applies exactly as it would to any other retry).</summary>
public sealed record RetryWorkflowExecutionNodeCommand(Guid ExecutionId, Guid ExecutionNodeId) : IRequest;
