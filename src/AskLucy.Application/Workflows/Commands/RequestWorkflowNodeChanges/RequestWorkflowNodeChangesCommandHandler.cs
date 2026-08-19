using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.RequestWorkflowNodeChanges;

public sealed class RequestWorkflowNodeChangesCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RequestWorkflowNodeChangesCommand, WorkflowApprovalDto>
{
    public async Task<WorkflowApprovalDto> Handle(RequestWorkflowNodeChangesCommand request, CancellationToken cancellationToken)
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

        approval.RequestChanges(userId);

        var node = execution.Nodes.FirstOrDefault(n => n.Id == approval.WorkflowExecutionNodeId);
        node?.Fail();

        var reason = $"The user requested changes to the intended action: {approval.IntendedActionDescription}. Comments: {request.Comments}";
        execution.Fail(reason);
        execution.RecordEvent(WorkflowExecutionEventType.ApprovalRejected, approval.WorkflowExecutionNodeId, "ChangesRequested", null);
        execution.RecordEvent(WorkflowExecutionEventType.WorkflowFailed, null, "Failed", null);
        auditLogRepository.Add(WorkflowAuditLog.Create(
            execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ApprovalDecided,
            JsonSerializer.Serialize(new { approval.Id, decision = "RequestChanges", request.Comments })));
        auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, userId, WorkflowAuditAction.ExecutionFailed, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowApprovalDto.Create(approval);
    }
}
