using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowStatistics;

public sealed class GetWorkflowStatisticsQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowStatisticsQuery, WorkflowStatisticsDto>
{
    public async Task<WorkflowStatisticsDto> Handle(GetWorkflowStatisticsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await executionRepository.GetStatisticsAsync(userId, cancellationToken);
    }
}
