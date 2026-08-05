using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Retrieval;
using MediatR;

namespace AskLucy.Application.Retrieval.Commands.UpdateConversationKnowledgeBases;

public sealed class UpdateConversationKnowledgeBasesCommandHandler(
    IUserChatRepository chatRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateConversationKnowledgeBasesCommand>
{
    public async Task Handle(UpdateConversationKnowledgeBasesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var requestedIds = request.KnowledgeBaseIds.Distinct().ToList();

        // A knowledgeBaseId the caller doesn't own must be rejected with 400 (DomainRuleViolationException),
        // never silently dropped/included (contracts/conversation-retrieval-api.md) — distinct from
        // KnowledgeBaseOwnershipGuard's "look like not-found" philosophy, since the caller is explicitly
        // naming these ids in their own request rather than looking up someone else's resource by id.
        if (requestedIds.Count > 0)
        {
            var ownedIds = await knowledgeBaseRepository.ResolveOwnedIdsAsync(userId, requestedIds, null, cancellationToken);
            if (ownedIds.Count != requestedIds.Count)
            {
                throw new DomainRuleViolationException("One or more knowledge bases are not owned by the caller.");
            }
        }

        await conversationKnowledgeBaseRepository.RemoveExceptAsync(request.ChatId, requestedIds, cancellationToken);

        var existingIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(request.ChatId, cancellationToken))
            .Select(l => l.KnowledgeBaseId)
            .ToHashSet();

        foreach (var knowledgeBaseId in requestedIds.Where(id => !existingIds.Contains(id)))
        {
            conversationKnowledgeBaseRepository.Add(ConversationKnowledgeBase.Create(request.ChatId, knowledgeBaseId, userId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
