using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// The many-to-many attachment between a conversation and the knowledge base(s) it draws context
/// from (spec.md FR-035, research.md Decision 10, data-model.md). Unique on
/// (<see cref="UserChatId"/>, <see cref="KnowledgeBaseId"/>) — enforced via a unique index in the
/// Persistence-layer configuration.
/// </summary>
public sealed class ConversationKnowledgeBase : BaseEntity
{
    public Guid UserChatId { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    public DateTime AttachedAtUtc { get; private set; }

    private ConversationKnowledgeBase()
    {
        // Required by EF Core materialization.
    }

    public static ConversationKnowledgeBase Create(Guid userChatId, Guid knowledgeBaseId, string actor)
    {
        var attachedAtUtc = DateTime.UtcNow;

        return new ConversationKnowledgeBase
        {
            Id = Guid.CreateVersion7(),
            UserChatId = userChatId,
            KnowledgeBaseId = knowledgeBaseId,
            AttachedAtUtc = attachedAtUtc,
            CreatedAtUtc = attachedAtUtc,
            CreatedBy = actor,
        };
    }
}
