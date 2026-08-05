using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Retrieval.Queries.GetConversationKnowledgeBases;

public sealed class GetConversationKnowledgeBasesQueryHandler(
    IUserChatRepository chatRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetConversationKnowledgeBasesQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(GetConversationKnowledgeBasesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var links = await conversationKnowledgeBaseRepository.GetByConversationAsync(request.ChatId, cancellationToken);
        return links.Select(l => l.KnowledgeBaseId).ToList();
    }
}
