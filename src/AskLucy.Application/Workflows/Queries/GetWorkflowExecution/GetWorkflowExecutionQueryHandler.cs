using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecution;

/// <summary>Cross-user access to another owner's execution is reported as a plain 404, never a 403
/// that would disclose existence, but the attempt is still recorded — mirrors GetAgentExecutionQueryHandler
/// exactly (spec 020 precedent).</summary>
public sealed class GetWorkflowExecutionQueryHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowExecutionQuery, WorkflowExecutionDetailDto>
{
    public async Task<WorkflowExecutionDetailDto> Handle(GetWorkflowExecutionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = await executionRepository.GetByIdForUserAsync(request.Id, userId, cancellationToken);
        if (execution is null)
        {
            var anyOwnersExecution = await executionRepository.GetByIdAsync(request.Id, cancellationToken);
            if (anyOwnersExecution is not null)
            {
                auditLogRepository.Add(WorkflowAuditLog.Create(anyOwnersExecution.WorkflowId, request.Id, userId, WorkflowAuditAction.CrossUserAccessAttempted, "{}"));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            throw new KeyNotFoundException("Workflow execution not found.");
        }

        return WorkflowExecutionDetailDto.Create(execution);
    }
}
