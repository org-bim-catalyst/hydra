using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionUsage;

public sealed class GetAgentExecutionUsageQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentExecutionUsageQuery, AgentExecutionUsageDto>
{
    public async Task<AgentExecutionUsageDto> Handle(GetAgentExecutionUsageQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return AgentExecutionUsageDto.Create(execution.Usage, execution.Cost);
    }
}
