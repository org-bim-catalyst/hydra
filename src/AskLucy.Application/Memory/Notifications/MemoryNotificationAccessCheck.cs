using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.Memory.Notifications;

/// <summary>T091 — the <c>Memory</c> related-item type's <see cref="INotificationAccessCheck"/> (research R24). Reuses <see cref="AskLucy.Domain.Memory.Memory.IsOwnedBy"/>, the same predicate <c>MemoryOwnershipGuard</c> checks, without that guard's throw-as-404 behavior. Deliberately uses <see cref="IMemoryRepository.GetByIdAsync"/> rather than <see cref="IMemoryRepository.GetActiveByIdsAsync"/>/<see cref="IMemoryRepository.GetByIdsAsync"/> — those two additionally filter by lifecycle/conflict state for retrieval purposes, which isn't this check's concern; an archived or conflicted memory the caller still owns should still open.</summary>
public sealed class MemoryNotificationAccessCheck(IMemoryRepository memoryRepository) : INotificationAccessCheck
{
    public string ItemType => "Memory";

    public async Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(itemId, out var memoryId))
        {
            return false;
        }

        var memory = await memoryRepository.GetByIdAsync(memoryId, cancellationToken);
        return memory is not null && memory.IsOwnedBy(userId);
    }

    public async Task<IReadOnlySet<string>> GetAvailableAsync(string userId, IReadOnlyCollection<string> itemIds, CancellationToken cancellationToken)
    {
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemId in itemIds)
        {
            if (await CanAccessAsync(userId, itemId, cancellationToken))
            {
                available.Add(itemId);
            }
        }

        return available;
    }
}
