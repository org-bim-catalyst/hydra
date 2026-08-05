using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ConversationKnowledgeBaseRepository(AskLucyDbContext dbContext) : IConversationKnowledgeBaseRepository
{
    public async Task<IReadOnlyList<ConversationKnowledgeBase>> GetByConversationAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.ConversationKnowledgeBases
            .Where(c => c.UserChatId == userChatId)
            .ToListAsync(cancellationToken);

    public void Add(ConversationKnowledgeBase link) => dbContext.ConversationKnowledgeBases.Add(link);

    public async Task RemoveExceptAsync(Guid userChatId, IReadOnlyCollection<Guid> keepKnowledgeBaseIds, CancellationToken cancellationToken = default)
    {
        var toRemove = await dbContext.ConversationKnowledgeBases
            .Where(c => c.UserChatId == userChatId && !keepKnowledgeBaseIds.Contains(c.KnowledgeBaseId))
            .ToListAsync(cancellationToken);

        dbContext.ConversationKnowledgeBases.RemoveRange(toRemove);
    }
}
