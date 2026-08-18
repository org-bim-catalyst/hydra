using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionEvents;

public sealed class GetAgentExecutionEventsQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentExecutionEventsQuery, IReadOnlyList<AgentExecutionEventDto>>
{
    public async Task<IReadOnlyList<AgentExecutionEventDto>> Handle(GetAgentExecutionEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return execution.Events
            .Where(e => request.Since is null || e.OccurredAtUtc > request.Since)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(AgentExecutionEventDto.Create)
            .ToList();
    }
}
