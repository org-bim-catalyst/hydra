using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.KnowledgeBases.Notifications;

/// <summary>T091 — the <c>KnowledgeBase</c> related-item type's <see cref="INotificationAccessCheck"/> (research R24). Currently unreachable in production — <c>knowledge-base.updated</c> is the only type declaring this item type and stays <c>IsEmitted=false</c> (T089) — but registered up front so the dispatcher never fails closed on it once the type is turned on.</summary>
public sealed class KnowledgeBaseNotificationAccessCheck(IKnowledgeBaseRepository knowledgeBaseRepository) : INotificationAccessCheck
{
    public string ItemType => "KnowledgeBase";

    public async Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(itemId, out var knowledgeBaseId))
        {
            return false;
        }

        var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId, cancellationToken);
        return knowledgeBase is not null && knowledgeBase.IsOwnedBy(userId);
    }

    public async Task<IReadOnlySet<string>> GetAvailableAsync(string userId, IReadOnlyCollection<string> itemIds, CancellationToken cancellationToken)
    {
        var ids = itemIds.Select(id => Guid.TryParse(id, out var parsed) ? (Guid?)parsed : null).Where(id => id is not null).Select(id => id!.Value).ToList();
        var knowledgeBases = await knowledgeBaseRepository.GetByIdsAsync(ids, cancellationToken);
        return knowledgeBases.Where(kb => kb.IsOwnedBy(userId)).Select(kb => kb.Id.ToString()).ToHashSet(StringComparer.Ordinal);
    }
}
