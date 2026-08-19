using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.CancelWorkflowExecution;

public sealed class CancelWorkflowExecutionCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowExecutionNotifier notifier,
    IWorkflowAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CancelWorkflowExecutionCommand>
{
    private const string Reason = "Cancelled by user.";

    public async Task Handle(CancelWorkflowExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        if (execution.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled or WorkflowExecutionStatus.TimedOut)
        {
            return; // Already terminal — nothing to cancel.
        }

        execution.Cancel(Reason);
        execution.RecordEvent(WorkflowExecutionEventType.WorkflowCancelled, workflowNodeId: null, "Cancelled", null);
        auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ExecutionCancelled, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyWorkflowCancelledAsync(userId, execution.Id, Reason, DateTime.UtcNow, cancellationToken);
    }
}
