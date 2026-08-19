using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Workflows.Commands.StartWorkflowExecution;

public sealed class StartWorkflowExecutionCommandHandler(
    IWorkflowRepository workflowRepository,
    IWorkflowExecutionRepository executionRepository,
    IWorkflowPolicyRepository policyRepository,
    IWorkflowExecutionRunner runner,
    IWorkflowAuditLogRepository auditLogRepository,
    IOptions<WorkflowRuntimeOptions> workflowRuntimeOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<StartWorkflowExecutionCommand, WorkflowExecutionSummaryDto>
{
    public async Task<WorkflowExecutionSummaryDto> Handle(StartWorkflowExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.WorkflowId, userId, cancellationToken), userId);

        // FR-002 — Deprecate is a one-way lifecycle stage: no new manual OR event-triggered
        // executions start against it (already-running executions are unaffected). Disabled only
        // stops event-trigger dispatch (Acceptance Scenario 9.3) — a manual start is still allowed.
        if (workflow.Status == WorkflowStatus.Deprecated)
        {
            throw new DomainRuleViolationException("This workflow has been deprecated and no longer accepts new executions.");
        }

        // FR-069/FR-070 — checked before any execution row is created, so a rejected attempt
        // never leaves one behind (mirrors StartAgentExecutionCommandHandler's precedent).
        var userLimit = await policyRepository.GetUserExecutionLimitAsync(userId, cancellationToken);
        var maxConcurrentExecutions = userLimit?.MaxConcurrentExecutions ?? workflowRuntimeOptions.Value.DefaultMaxConcurrentExecutions;
        var activeCount = await executionRepository.CountActiveByUserAsync(userId, cancellationToken);
        if (activeCount >= maxConcurrentExecutions)
        {
            throw new WorkflowConcurrencyLimitExceededException(maxConcurrentExecutions);
        }

        var versionNumber = request.WorkflowVersionNumber ?? workflow.PublishedVersionNumber
            ?? throw new DomainRuleViolationException("This workflow has no published version yet — publish it before starting an execution.");
        var version = await workflowRepository.GetVersionAsync(workflow.Id, versionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow version not found.");

        var execution = WorkflowExecution.Create(
            workflow.Id, version.Id, userId, request.TriggerType, triggeringEventReferenceJson: null, request.InputsJson, userId);

        executionRepository.Add(execution);
        auditLogRepository.Add(WorkflowAuditLog.Create(workflow.Id, execution.Id, userId, WorkflowAuditAction.ExecutionStarted, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await runner.EnqueueAsync(execution.Id, cancellationToken);

        return WorkflowExecutionSummaryDto.Create(execution);
    }
}
