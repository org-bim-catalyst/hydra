using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.RejectWorkflowNode;

public sealed class RejectWorkflowNodeCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RejectWorkflowNodeCommand, WorkflowApprovalDto>
{
    public async Task<WorkflowApprovalDto> Handle(RejectWorkflowNodeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.WorkflowExecutionId, userId, cancellationToken), userId);

        var approval = execution.Approvals.FirstOrDefault(a => a.Id == request.ApprovalId)
            ?? throw new KeyNotFoundException("Approval not found.");

        if (approval.Decision != WorkflowApprovalDecision.Pending)
        {
            throw new DomainRuleViolationException("This approval has already been decided.");
        }

        approval.Reject(userId);

        var node = execution.Nodes.FirstOrDefault(n => n.Id == approval.WorkflowExecutionNodeId);
        node?.Fail();

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"The user rejected the intended action: {approval.IntendedActionDescription}."
            : $"The user rejected the intended action: {approval.IntendedActionDescription}. Reason: {request.Reason}";
        execution.Fail(reason);
        execution.RecordEvent(WorkflowExecutionEventType.ApprovalRejected, approval.WorkflowExecutionNodeId, "Rejected", null);
        execution.RecordEvent(WorkflowExecutionEventType.WorkflowFailed, null, "Failed", null);
        auditLogRepository.Add(WorkflowAuditLog.Create(
            execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ApprovalDecided,
            JsonSerializer.Serialize(new { approval.Id, decision = "Rejected" })));
        auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ExecutionFailed, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowApprovalDto.Create(approval);
    }
}
