using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Authorization;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotification;

public sealed class GetNotificationQueryHandler(
    INotificationRepository notificationRepository,
    IEnumerable<INotificationAccessCheck> accessChecks,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetNotificationQuery, NotificationDetailDto>
{
    public async Task<NotificationDetailDto> Handle(GetNotificationQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var notification = NotificationOwnershipGuard.EnsureOwnedBy(
            await notificationRepository.GetByIdAsync(request.NotificationId, cancellationToken), userId);

        var availableByType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        if (notification.RelatedItemType is { } type && notification.RelatedItemId is { } id)
        {
            var check = accessChecks.FirstOrDefault(c => c.ItemType == type);
            if (check is not null)
            {
                var available = await check.CanAccessAsync(userId, id, cancellationToken);
                availableByType[type] = available ? new HashSet<string>(StringComparer.Ordinal) { id } : new HashSet<string>(StringComparer.Ordinal);
            }

            // No registered check for this related-item type: leave it out of the map, which
            // NotificationMapper.ToListItemDto treats as available=true (T061).
        }

        var item = NotificationMapper.ToListItemDto(notification, availableByType);
        var metadata = ParseMetadata(notification.MetadataJson);
        return NotificationDetailDto.FromListItem(item, metadata);
    }

    private static Dictionary<string, string?> ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(metadataJson)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // Malformed metadata never blocks the notification itself from being viewed — it's
            // display-only (FR-013), not load-bearing.
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }
}
