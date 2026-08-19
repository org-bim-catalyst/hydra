using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflow;

public sealed class UpdateWorkflowCommandHandler(
    IWorkflowRepository workflowRepository, IWorkflowAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(UpdateWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        if (await workflowRepository.ExistsWithNameForOwnerAsync(userId, request.Name.Trim(), excludingWorkflowId: workflow.Id, cancellationToken))
        {
            throw new DuplicateResourceException($"A workflow named '{request.Name}' already exists.");
        }

        workflow.UpdateDraft(request.Name, request.Description, request.DraftDefinitionJson, userId);
        if (request.EventTriggerConfigurationJson is not null)
        {
            workflow.SetEventTriggerConfiguration(request.EventTriggerConfigurationJson, userId);
        }

        auditLogRepository.Add(WorkflowAuditLog.Create(workflow.Id, null, userId, WorkflowAuditAction.WorkflowModified, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
