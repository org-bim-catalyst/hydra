using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>
/// contracts/notifications-api.md GET /notifications/{id}: the same flat shape as
/// <see cref="NotificationListItemDto"/>, plus non-sensitive <see cref="Metadata"/> (FR-013). A
/// separate record rather than nesting the list item, because the wire shape is flat.
/// </summary>
public sealed record NotificationDetailDto(
    Guid Id,
    NotificationCategory Category,
    string Type,
    string Title,
    string Message,
    NotificationPriority Priority,
    NotificationStatus Status,
    string Language,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    DateTime? ExpiresAtUtc,
    NotificationActionDto? Action,
    NotificationRelatedItemDto? RelatedItem,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public static NotificationDetailDto FromListItem(NotificationListItemDto item, IReadOnlyDictionary<string, string?> metadata) => new(
        item.Id, item.Category, item.Type, item.Title, item.Message, item.Priority, item.Status, item.Language,
        item.CreatedAtUtc, item.ReadAtUtc, item.ExpiresAtUtc, item.Action, item.RelatedItem, metadata);
}
