using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Application.Workflows.Validation;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.PublishWorkflowVersion;

/// <summary>
/// Validates the current draft (research.md Decision 19), then materializes it into structured
/// <see cref="WorkflowNode"/>/<see cref="WorkflowConnection"/>/<see cref="WorkflowVariable"/> rows
/// on a new, immutable <see cref="WorkflowVersion"/> — the JSON parsing happens here (Application),
/// never in <see cref="Workflow.Publish"/> itself (Domain never parses raw JSON).
/// </summary>
public sealed class PublishWorkflowVersionCommandHandler(
    IWorkflowRepository workflowRepository, WorkflowGraphValidator validator, IWorkflowAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<PublishWorkflowVersionCommand, WorkflowVersionDto>
{
    public async Task<WorkflowVersionDto> Handle(PublishWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.WorkflowId, userId, cancellationToken), userId);

        if (!WorkflowDraftDefinition.TryParse(workflow.DraftDefinitionJson, out var draft, out var parseError))
        {
            throw new WorkflowValidationFailedException([new WorkflowValidationFailure(null, parseError!)]);
        }

        var issues = validator.Validate(draft!);
        if (issues.Count > 0)
        {
            throw new WorkflowValidationFailedException(issues.Select(i => new WorkflowValidationFailure(i.NodeKey, i.Message)).ToList());
        }

        var version = workflow.Publish(
            draft!.Nodes.Select(n => n.ToSpec()).ToList(),
            draft.Connections.Select(c => c.ToSpec()).ToList(),
            draft.Variables.Select(v => v.ToSpec()).ToList(),
            draft.InputsSchemaJson, draft.OutputsSchemaJson, draft.ErrorPolicyJson, draft.ExecutionPolicyJson, draft.SecurityPolicyJson,
            request.ChangeDescription, userId);

        auditLogRepository.Add(WorkflowAuditLog.Create(workflow.Id, null, userId, WorkflowAuditAction.WorkflowPublished, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowVersionDto.Create(version);
    }
}
