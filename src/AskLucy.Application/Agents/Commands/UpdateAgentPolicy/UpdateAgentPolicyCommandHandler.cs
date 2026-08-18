using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.UpdateAgentPolicy;

public sealed class UpdateAgentPolicyCommandHandler(IAgentPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateAgentPolicyCommand, AgentPolicyDto>
{
    public async Task<AgentPolicyDto> Handle(UpdateAgentPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var policy = await policyRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Agent policy not found.");

        policy.Update(request.Name, request.Description, request.ConditionsJson, userId);
        policy.SetEnabled(request.IsEnabled, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentPolicyDto.Create(policy);
    }
}
