namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// Implemented by the module that owns a related-item type (research R24). The dispatcher re-checks
/// access before materializing, and the center marks items the user can no longer open.
/// </summary>
public interface INotificationAccessCheck
{
    /// <summary>The <see cref="RelatedItem.Type"/> this check answers for.</summary>
    string ItemType { get; }

    Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="itemIds"/> still exist and are visible to <paramref name="userId"/>.</summary>
    Task<IReadOnlySet<string>> GetAvailableAsync(string userId, IReadOnlyCollection<string> itemIds, CancellationToken cancellationToken);
}
