using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentApproval;

public sealed class GetAgentApprovalQueryHandler(IAgentExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentApprovalQuery, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(GetAgentApprovalQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.AgentExecutionId, userId, cancellationToken), userId);

        var approval = execution.Approvals.FirstOrDefault(a => a.Id == request.ApprovalId)
            ?? throw new KeyNotFoundException("Approval not found.");

        return AgentApprovalDto.Create(approval);
    }
}
