using MediatR;

namespace AskLucy.Application.Workflows.Commands.RequestWorkflowNodeChanges;

/// <summary>
/// A distinct third approval decision (spec.md "Support: Approve / Reject / Request Changes / Cancel")
/// with no Agent Runtime precedent — the reviewer isn't rejecting the intended action outright, they're
/// asking for it to be reconsidered before it can run. There is no re-planning capability in the
/// workflow model to act on that feedback, so — like Reject — it terminates the execution directly
/// rather than re-enqueuing the runner; the distinction from Reject is purely in the recorded decision
/// and the required <see cref="Comments"/> describing what needs to change.
/// </summary>
public sealed record RequestWorkflowNodeChangesCommand(Guid WorkflowExecutionId, Guid ApprovalId, string Comments) : IRequest<WorkflowApprovalDto>;
