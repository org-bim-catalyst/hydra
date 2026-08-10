using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Agents.Commands.RejectAgentAction;

public sealed class RejectAgentActionCommandHandler(
    IAgentExecutionRepository executionRepository, IAgentExecutionNotifier notifier, IAgentAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RejectAgentActionCommand, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(RejectAgentActionCommand request, CancellationToken cancellationToken)
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

        approval.Reject(userId);

        if (approval.AgentToolCallId is { } toolCallId)
        {
            var toolCall = await executionRepository.GetToolCallByIdAsync(toolCallId, cancellationToken);
            if (toolCall is not null)
            {
                toolCall.Fail("Rejected by user.");
                var step = execution.Steps.FirstOrDefault(s => s.Id == toolCall.AgentExecutionStepId);
                step?.Cancel();
            }
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"The user rejected the intended action: {approval.IntendedActionDescription}."
            : $"The user rejected the intended action: {approval.IntendedActionDescription}. Reason: {request.Reason}";
        execution.Fail(reason);
        execution.RecordEvent(AgentExecutionEventType.ApprovalRejected, execution.AgentVersionId, null, "Rejected", null);
        execution.RecordEvent(AgentExecutionEventType.ExecutionFailed, execution.AgentVersionId, null, "Failed", null);
        auditLogRepository.Add(AgentAuditLog.Create(execution.Id, userId, AgentAuditAction.ApprovalDecided, JsonSerializer.Serialize(new { approval.Id, decision = "Rejected" })));
        auditLogRepository.Add(AgentAuditLog.Create(execution.Id, userId, AgentAuditAction.ExecutionFailed, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var occurredAtUtc = DateTime.UtcNow;
        await notifier.NotifyApprovalRejectedAsync(userId, execution.Id, approval.Id, userId, occurredAtUtc, cancellationToken);
        await notifier.NotifyExecutionFailedAsync(userId, execution.Id, reason, occurredAtUtc, cancellationToken);

        return AgentApprovalDto.Create(approval);
    }
}
