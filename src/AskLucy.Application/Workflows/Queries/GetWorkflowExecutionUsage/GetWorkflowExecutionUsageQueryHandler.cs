using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionUsage;

public sealed class GetWorkflowExecutionUsageQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowExecutionUsageQuery, WorkflowExecutionUsageDto>
{
    public async Task<WorkflowExecutionUsageDto> Handle(GetWorkflowExecutionUsageQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return WorkflowExecutionUsageDto.Create(execution.Usage, execution.Cost);
    }
}
