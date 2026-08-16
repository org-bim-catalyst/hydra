using MediatR;

namespace AskLucy.Application.Workflows.Commands.ApproveWorkflowNode;

/// <summary>Interactive approval of a paused Human Approval node or High/Critical-risk capability node (spec.md User Story 5) — re-enqueues the execution to resume from where it paused.</summary>
public sealed record ApproveWorkflowNodeCommand(Guid WorkflowExecutionId, Guid ApprovalId) : IRequest<WorkflowApprovalDto>;
