using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.Agents.Notifications;

/// <summary>T091 — the <c>AgentExecution</c> related-item type's <see cref="INotificationAccessCheck"/> (research R24). Uses <see cref="IAgentExecutionRepository.GetByIdForUserAsync"/> directly rather than <c>AgentExecutionOwnershipGuard</c> — the guard throws as a 404, which is wrong here: an inaccessible item just means "hide it."</summary>
public sealed class AgentExecutionNotificationAccessCheck(IAgentExecutionRepository executionRepository) : INotificationAccessCheck
{
    public string ItemType => "AgentExecution";

    public async Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(itemId, out var executionId))
        {
            return false;
        }

        return await executionRepository.GetByIdForUserAsync(executionId, userId, cancellationToken) is not null;
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
