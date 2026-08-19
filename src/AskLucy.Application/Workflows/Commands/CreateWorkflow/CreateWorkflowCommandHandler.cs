using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflow;

public sealed class CreateWorkflowCommandHandler(
    IWorkflowRepository workflowRepository, IWorkflowAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(CreateWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // FR-001 — unique per owner, case-insensitive (Clarifications, spec 019 precedent).
        if (await workflowRepository.ExistsWithNameForOwnerAsync(userId, request.Name.Trim(), excludingWorkflowId: null, cancellationToken))
        {
            throw new DuplicateResourceException($"A workflow named '{request.Name}' already exists.");
        }

        var workflow = Workflow.Create(userId, request.Name, request.Description, request.WorkflowType, userId);
        if (request.EventTriggerConfigurationJson is not null)
        {
            workflow.SetEventTriggerConfiguration(request.EventTriggerConfigurationJson, userId);
        }

        workflowRepository.Add(workflow);
        auditLogRepository.Add(WorkflowAuditLog.Create(workflow.Id, null, userId, WorkflowAuditAction.WorkflowCreated, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
