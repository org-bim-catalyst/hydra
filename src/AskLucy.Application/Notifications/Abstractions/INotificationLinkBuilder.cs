using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Builds action links from a type's route template (research R11). Never produces an external host.</summary>
public interface INotificationLinkBuilder
{
    /// <summary>The app-relative route for the in-app center, or null when the type has no route or the related item can't fill it.</summary>
    string? BuildRelative(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId);

    /// <summary>The absolute URL on the configured frontend origin, for email.</summary>
    string? BuildAbsolute(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId);
}
