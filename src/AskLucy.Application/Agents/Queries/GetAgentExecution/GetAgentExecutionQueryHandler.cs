using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecution;

public sealed class GetAgentExecutionQueryHandler(
    IAgentExecutionRepository executionRepository, IAgentRepository agentRepository,
    IAgentAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentExecutionQuery, AgentExecutionDetailDto>
{
    public async Task<AgentExecutionDetailDto> Handle(GetAgentExecutionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = await executionRepository.GetByIdForUserAsync(request.Id, userId, cancellationToken);

        if (execution is null)
        {
            // Distinguishes a genuine 404 from another user's execution (spec.md FR-048/SC-010
            // Edge Cases: "the attempt is recorded as a security event") — this extra lookup only
            // runs on the not-found path, never on the hot, repeatedly-polled happy path.
            var anyOwnersExecution = await executionRepository.GetByIdAsync(request.Id, cancellationToken);
            if (anyOwnersExecution is not null)
            {
                auditLogRepository.Add(AgentAuditLog.Create(request.Id, userId, AgentAuditAction.CrossUserAccessAttempted, "{}"));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            throw new KeyNotFoundException("Agent execution not found.");
        }

        AgentExecutionOwnershipGuard.EnsureOwnedBy(execution, userId);

        var agentVersion = await agentRepository.GetVersionByIdAsync(execution.AgentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Agent version not found.");

        return AgentExecutionDetailDto.Create(execution, agentVersion.VersionNumber);
    }
}
