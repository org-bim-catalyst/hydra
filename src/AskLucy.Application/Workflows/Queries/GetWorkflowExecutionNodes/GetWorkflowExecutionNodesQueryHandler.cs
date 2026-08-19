using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionNodes;

public sealed class GetWorkflowExecutionNodesQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowExecutionNodesQuery, IReadOnlyList<WorkflowExecutionNodeDto>>
{
    public async Task<IReadOnlyList<WorkflowExecutionNodeDto>> Handle(GetWorkflowExecutionNodesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return execution.Nodes.OrderBy(n => n.CreatedAtUtc).Select(WorkflowExecutionNodeDto.Create).ToList();
    }
}
