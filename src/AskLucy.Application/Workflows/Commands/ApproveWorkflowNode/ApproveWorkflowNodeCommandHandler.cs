using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.ApproveWorkflowNode;

public sealed class ApproveWorkflowNodeCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowExecutionRunner runner,
    IWorkflowAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ApproveWorkflowNodeCommand, WorkflowApprovalDto>
{
    public async Task<WorkflowApprovalDto> Handle(ApproveWorkflowNodeCommand request, CancellationToken cancellationToken)
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

        approval.Approve(userId);
        execution.Resume();
        execution.RecordEvent(WorkflowExecutionEventType.ApprovalGranted, approval.WorkflowExecutionNodeId, "Approved", JsonSerializer.Serialize(new { wasPolicyBased = false }));
        auditLogRepository.Add(WorkflowAuditLog.Create(
            execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ApprovalDecided,
            JsonSerializer.Serialize(new { approval.Id, decision = "Approved" })));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await runner.EnqueueAsync(execution.Id, cancellationToken);

        return WorkflowApprovalDto.Create(approval);
    }
}
