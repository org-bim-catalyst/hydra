using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.CreateAgent;

public sealed class CreateAgentCommandHandler(
    IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateAgentCommand, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(CreateAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var agent = Agent.Create(
            userId, request.Name, request.Description, request.AgentType, request.Instructions.ToDomain(),
            request.ModelProviderId, request.ModelId, request.OutputFormat, request.ExecutionPolicy.ToDomain(), userId);

        agentRepository.Add(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentDetailDto.Create(agent);
    }
}
