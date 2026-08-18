using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentExecutions;

public sealed class ListAgentExecutionsQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListAgentExecutionsQuery, PagedResult<AgentExecutionSummaryDto>>
{
    public async Task<PagedResult<AgentExecutionSummaryDto>> Handle(ListAgentExecutionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await executionRepository.ListByUserAsync(
            userId, request.AgentId, request.Status, request.IsTestExecution, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<AgentExecutionSummaryDto>(items.Select(AgentExecutionSummaryDto.Create).ToList(), nextCursor);
    }
}
