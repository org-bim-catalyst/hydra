using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Agents.Commands.ApproveAgentAction;

public sealed class ApproveAgentActionCommandHandler(
    IAgentExecutionRepository executionRepository, IAgentExecutionRunner runner, IAgentExecutionNotifier notifier,
    IAgentAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ApproveAgentActionCommand, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(ApproveAgentActionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.AgentExecutionId, userId, cancellationToken), userId);

        var approval = execution.Approvals.FirstOrDefault(a => a.Id == request.ApprovalId)
            ?? throw new KeyNotFoundException("Approval not found.");

        if (approval.Decision != AgentApprovalDecision.Pending)
        {
            throw new DomainRuleViolationException("This approval has already been decided.");
        }

        approval.Approve(userId);
        execution.Resume();
        execution.RecordEvent(AgentExecutionEventType.ApprovalGranted, execution.AgentVersionId, null, "Approved", JsonSerializer.Serialize(new { wasPolicyBased = false }));
        auditLogRepository.Add(AgentAuditLog.Create(execution.Id, userId, AgentAuditAction.ApprovalDecided, JsonSerializer.Serialize(new { approval.Id, decision = "Approved" })));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyApprovalGrantedAsync(userId, execution.Id, approval.Id, userId, wasPolicyBased: false, DateTime.UtcNow, cancellationToken);
        await runner.EnqueueAsync(execution.Id, cancellationToken);

        return AgentApprovalDto.Create(approval);
    }
}
