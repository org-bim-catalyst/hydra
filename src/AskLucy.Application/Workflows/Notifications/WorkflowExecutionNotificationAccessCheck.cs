using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.Workflows.Notifications;

/// <summary>T091 — the <c>WorkflowExecution</c> related-item type's <see cref="INotificationAccessCheck"/> (research R24). Mirrors <see cref="AskLucy.Application.Agents.Notifications.AgentExecutionNotificationAccessCheck"/>: uses <see cref="IWorkflowExecutionRepository.GetByIdForUserAsync"/> directly rather than <c>WorkflowExecutionOwnershipGuard</c>, whose 404-throwing behavior doesn't fit "hide it" semantics.</summary>
public sealed class WorkflowExecutionNotificationAccessCheck(IWorkflowExecutionRepository executionRepository) : INotificationAccessCheck
{
    public string ItemType => "WorkflowExecution";

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
