using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.UpdateAgent;

public sealed class UpdateAgentCommandHandler(
    IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateAgentCommand, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(UpdateAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        agent.UpdateDraft(
            request.Name, request.Description, request.AgentType, request.Instructions.ToDomain(),
            request.ModelProviderId, request.ModelId, request.OutputFormat, request.ExecutionPolicy.ToDomain(), userId);

        if (request.Tools is not null)
        {
            agent.ClearTools(userId);
            foreach (var tool in request.Tools)
            {
                agent.AddTool(tool.ToolName, tool.ConfigurationJson, userId);
            }
        }

        if (request.KnowledgeBaseIds is not null)
        {
            agent.ClearKnowledgeBases(userId);
            foreach (var knowledgeBaseId in request.KnowledgeBaseIds)
            {
                agent.AddKnowledgeBase(knowledgeBaseId, userId);
            }
        }

        if (request.MemoryPolicy is { } memoryPolicy)
        {
            agent.SetMemoryPolicy(memoryPolicy.AllowRead, memoryPolicy.AllowWriteProposals, memoryPolicy.PreApprovedCategoriesJson, userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentDetailDto.Create(agent);
    }
}
