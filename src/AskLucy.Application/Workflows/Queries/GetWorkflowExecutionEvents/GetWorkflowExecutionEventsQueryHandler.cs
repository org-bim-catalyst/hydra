using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionEvents;

public sealed class GetWorkflowExecutionEventsQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowExecutionEventsQuery, IReadOnlyList<WorkflowExecutionEventDto>>
{
    public async Task<IReadOnlyList<WorkflowExecutionEventDto>> Handle(GetWorkflowExecutionEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return execution.Events
            .Where(e => request.Since is null || e.OccurredAtUtc > request.Since)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(WorkflowExecutionEventDto.Create)
            .ToList();
    }
}
