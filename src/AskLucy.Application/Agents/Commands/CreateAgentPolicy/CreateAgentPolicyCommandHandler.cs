using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.CreateAgentPolicy;

public sealed class CreateAgentPolicyCommandHandler(IAgentPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateAgentPolicyCommand, AgentPolicyDto>
{
    public async Task<AgentPolicyDto> Handle(CreateAgentPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var policy = AgentPolicy.Create(request.Name, request.Description, request.ToolName, request.ConditionsJson, userId);
        policyRepository.Add(policy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentPolicyDto.Create(policy);
    }
}
