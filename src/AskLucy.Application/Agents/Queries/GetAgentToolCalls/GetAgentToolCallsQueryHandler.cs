using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentToolCalls;

public sealed class GetAgentToolCallsQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentToolCallsQuery, IReadOnlyList<AgentToolCallDto>>
{
    public async Task<IReadOnlyList<AgentToolCallDto>> Handle(GetAgentToolCallsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        var toolCalls = await executionRepository.ListToolCallsByStepIdsAsync(execution.Steps.Select(s => s.Id).ToList(), cancellationToken);

        return toolCalls.OrderBy(tc => tc.StartedAtUtc).Select(AgentToolCallDto.Create).ToList();
    }
}
