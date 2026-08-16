using MediatR;

namespace AskLucy.Application.Workflows.Commands.RejectWorkflowNode;

/// <summary>Interactive rejection of a paused Human Approval node or High/Critical-risk capability node (spec.md User Story 5) — the node's action never executes; the workflow execution ends with an explanation.</summary>
public sealed record RejectWorkflowNodeCommand(Guid WorkflowExecutionId, Guid ApprovalId, string? Reason) : IRequest<WorkflowApprovalDto>;
