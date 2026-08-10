using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionSteps;

public sealed class GetAgentExecutionStepsQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentExecutionStepsQuery, IReadOnlyList<AgentExecutionStepDto>>
{
    public async Task<IReadOnlyList<AgentExecutionStepDto>> Handle(GetAgentExecutionStepsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        return execution.Steps.OrderBy(s => s.StepIndex).Select(AgentExecutionStepDto.Create).ToList();
    }
}
