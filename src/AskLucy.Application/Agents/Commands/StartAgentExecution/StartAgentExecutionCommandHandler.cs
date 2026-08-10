using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.CreateUserChat;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Options;
using UserChat = AskLucy.Domain.Chats.UserChat;

namespace AskLucy.Application.Agents.Commands.StartAgentExecution;

/// <summary>
/// Handles conversation-mode resolution inline (FR-051/FR-052 — <c>NewConversation</c> creates a
/// conversation via <c>ISender.Send(CreateUserChatCommand)</c>, safe here because this handler
/// runs under a real HTTP-authenticated request, unlike the background orchestrator which must
/// never depend on <c>ICurrentUserAccessor</c> — see <see cref="Runtime.AgentExecutionOrchestrator"/>'s
/// doc comment).
/// </summary>
public sealed class StartAgentExecutionCommandHandler(
    IAgentRepository agentRepository,
    IAgentExecutionRepository executionRepository,
    IAgentAuditLogRepository auditLogRepository,
    IAgentPolicyRepository policyRepository,
    IUserChatRepository chatRepository,
    IAgentExecutionRunner runner,
    ISender sender,
    IOptions<AgentRuntimeOptions> agentRuntimeOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<StartAgentExecutionCommand, AgentExecutionSummaryDto>
{
    public async Task<AgentExecutionSummaryDto> Handle(StartAgentExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.AgentId, userId, cancellationToken), userId);

        // FR-042/FR-043 — checked before any side effect (e.g. creating a NewConversation-mode
        // conversation) so a rejected request never leaves one behind.
        var userLimit = await policyRepository.GetUserExecutionLimitAsync(userId, cancellationToken);
        var maxConcurrentExecutions = userLimit?.MaxConcurrentExecutions ?? agentRuntimeOptions.Value.DefaultMaxConcurrentExecutions;
        var activeCount = await executionRepository.CountActiveByUserAsync(userId, cancellationToken);
        if (activeCount >= maxConcurrentExecutions)
        {
            throw new AgentConcurrencyLimitExceededException(maxConcurrentExecutions);
        }

        var versionNumber = request.AgentVersionNumber ?? agent.PublishedVersionNumber
            ?? throw new DomainRuleViolationException("This agent has no published version yet — publish it before starting an execution.");
        var agentVersion = await agentRepository.GetVersionAsync(agent.Id, versionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Agent version not found.");

        Guid? userChatId = request.ConversationIntegrationMode switch
        {
            AgentConversationIntegrationMode.Standalone => null,
            AgentConversationIntegrationMode.NewConversation => (await sender.Send(
                new CreateUserChatCommand($"Agent: {agent.Name}", SessionId: null), cancellationToken)).Id,
            AgentConversationIntegrationMode.ExistingConversation => ResolveExistingConversationId(
                ChatOwnershipGuard.EnsureOwnedBy(
                    await chatRepository.GetByIdAsync(request.UserChatId ?? throw new DomainRuleViolationException(
                        "An existing conversation id is required when conversationIntegrationMode is ExistingConversation."), cancellationToken),
                    userId)),
            _ => throw new DomainRuleViolationException("Unknown conversation integration mode."),
        };

        var execution = AgentExecution.Create(
            agent.Id, agentVersion.Id, userId, request.Objective, request.IsTestExecution,
            request.ConversationIntegrationMode, userChatId, userId);

        executionRepository.Add(execution);
        auditLogRepository.Add(AgentAuditLog.Create(execution.Id, userId, AgentAuditAction.PermissionChecked, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await runner.EnqueueAsync(execution.Id, cancellationToken);

        return AgentExecutionSummaryDto.Create(execution);
    }

    private static Guid ResolveExistingConversationId(UserChat chat) => chat.Id;
}
