using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotifications;

public sealed class GetNotificationsQueryHandler(
    INotificationRepository notificationRepository,
    IEnumerable<INotificationAccessCheck> accessChecks,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationListItemDto>>
{
    public async Task<PagedResult<NotificationListItemDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await notificationRepository.ListAsync(
            userId, request.Categories, request.State, request.Cursor, request.Limit, cancellationToken);

        var availableByType = await ResolveAvailabilityAsync(userId, items, cancellationToken);

        return new PagedResult<NotificationListItemDto>(
            items.Select(n => NotificationMapper.ToListItemDto(n, availableByType)).ToList(), nextCursor);
    }

    /// <summary>Batches the per-module availability lookup by related-item type (research R24), one call per type instead of one per item.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> ResolveAvailabilityAsync(
        string userId, IReadOnlyList<Notification> items, CancellationToken cancellationToken)
    {
        var itemIdsByType = items
            .Where(n => n.RelatedItemType is not null && n.RelatedItemId is not null)
            .GroupBy(n => n.RelatedItemType!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(n => n.RelatedItemId!).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        if (itemIdsByType.Count == 0)
        {
            return new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        }

        var checksByType = accessChecks.ToDictionary(c => c.ItemType, StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (itemType, itemIds) in itemIdsByType)
        {
            if (checksByType.TryGetValue(itemType, out var check))
            {
                result[itemType] = await check.GetAvailableAsync(userId, itemIds, cancellationToken);
            }

            // No registered check for this related-item type: leave it out of the map.
            // NotificationMapper.ToListItemDto treats an absent type as available=true (T061),
            // rather than flagging every such item "no longer available" before its module
            // wires up a check (Phase 4).
        }

        return result;
    }
}
