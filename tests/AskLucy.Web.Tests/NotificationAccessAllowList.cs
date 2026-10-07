using System.Collections.Concurrent;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Web.Tests;

/// <summary>
/// Related-item ids a test has declared accessible to everyone (specs/067 T080). Every test host in the process runs its own
/// outbox dispatcher against the one shared database, so any of them may claim an event; a "yes" that lived only in one test's host
/// would be answered "no" by the others, and the notification would be dropped. This list is read by the access checks of every host
/// (see <see cref="CustomWebApplicationFactory"/>), and only for ids that are fresh random GUIDs the declaring test generated, so no
/// other test's access check is affected.
/// </summary>
public static class NotificationAccessAllowList
{
    private static readonly ConcurrentDictionary<string, byte> Ids = new(StringComparer.Ordinal);

    public static void Allow(params Guid[] itemIds)
    {
        foreach (var id in itemIds)
        {
            Ids[id.ToString()] = 0;
        }
    }

    internal static bool IsAllowed(string itemId) => Ids.ContainsKey(itemId);

    /// <summary>Wraps a real access check: allow-listed ids pass, everything else is answered by the real check.</summary>
    internal sealed class Wrapper(INotificationAccessCheck inner) : INotificationAccessCheck
    {
        public string ItemType => inner.ItemType;

        public Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken) =>
            IsAllowed(itemId) ? Task.FromResult(true) : inner.CanAccessAsync(userId, itemId, cancellationToken);

        public async Task<IReadOnlySet<string>> GetAvailableAsync(string userId, IReadOnlyCollection<string> itemIds, CancellationToken cancellationToken)
        {
            var allowed = itemIds.Where(IsAllowed).ToList();
            var rest = itemIds.Where(id => !IsAllowed(id)).ToList();
            var available = rest.Count == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(await inner.GetAvailableAsync(userId, rest, cancellationToken), StringComparer.Ordinal);
            available.UnionWith(allowed);
            return available;
        }
    }
}
