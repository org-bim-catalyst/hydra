using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Identifies one template: at most one exists per type, channel and language (FR-038).</summary>
public sealed record NotificationTemplateKey(string Type, NotificationChannel Channel, string Language);

public interface INotificationTemplateRepository
{
    /// <summary>
    /// The published version for exactly <paramref name="type"/>, <paramref name="channel"/> and
    /// <paramref name="language"/>; null when there is none. Language fallback is the renderer's
    /// job, because it reports the language it actually rendered.
    /// </summary>
    Task<NotificationTemplateVersion?> GetPublishedVersionAsync(
        string type, NotificationChannel channel, string language, CancellationToken cancellationToken);

    /// <summary>Every template that exists, published or not; the seeder never touches these.</summary>
    Task<IReadOnlySet<NotificationTemplateKey>> GetExistingKeysAsync(CancellationToken cancellationToken);

    void Add(NotificationTemplate notificationTemplate);
}
