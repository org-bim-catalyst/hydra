using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="ConversationKnowledgeBase"/> (constitution §3 Repository rules).</summary>
public interface IConversationKnowledgeBaseRepository
{
    Task<IReadOnlyList<ConversationKnowledgeBase>> GetByConversationAsync(Guid userChatId, CancellationToken cancellationToken = default);

    void Add(ConversationKnowledgeBase link);

    /// <summary>FR-035 — removes the attachment(s) not present in <paramref name="keepKnowledgeBaseIds"/> for a full-replace update.</summary>
    Task RemoveExceptAsync(Guid userChatId, IReadOnlyCollection<Guid> keepKnowledgeBaseIds, CancellationToken cancellationToken = default);
}
