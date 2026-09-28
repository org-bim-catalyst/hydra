using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

public interface INotificationTemplateRepository
{
    /// <summary>
    /// The published version for <paramref name="type"/> and <paramref name="channel"/> in
    /// <paramref name="language"/>, falling back to <c>en</c>; null when neither is published.
    /// </summary>
    Task<NotificationTemplateVersion?> GetPublishedVersionAsync(
        string type, NotificationChannel channel, string language, CancellationToken cancellationToken);
}
