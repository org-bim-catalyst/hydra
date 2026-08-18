using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.SetAgentUserExecutionLimit;

public sealed class SetAgentUserExecutionLimitCommandHandler(IAgentPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<SetAgentUserExecutionLimitCommand, AgentUserExecutionLimitDto>
{
    public async Task<AgentUserExecutionLimitDto> Handle(SetAgentUserExecutionLimitCommand request, CancellationToken cancellationToken)
    {
        var adminUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var existing = await policyRepository.GetUserExecutionLimitAsync(request.UserId, cancellationToken);

        if (existing is null)
        {
            var limit = AgentUserExecutionLimit.Create(request.UserId, request.MaxConcurrentExecutions, adminUserId);
            policyRepository.AddUserExecutionLimit(limit);
        }
        else
        {
            existing.Update(request.MaxConcurrentExecutions, adminUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AgentUserExecutionLimitDto(request.UserId, request.MaxConcurrentExecutions);
    }
}
