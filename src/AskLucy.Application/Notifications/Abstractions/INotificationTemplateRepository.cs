using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Identifies one template: at most one exists per type, channel and language (FR-038).</summary>
public sealed record NotificationTemplateKey(string Type, NotificationChannel Channel, string Language);

/// <summary>Optional filters for the admin template list.</summary>
public sealed class NotificationTemplateFilter
{
    public NotificationCategory? Category { get; init; }

    public NotificationChannel? Channel { get; init; }

    public string? Language { get; init; }

    public string? Type { get; init; }
}

/// <summary>One list row: the template and what its versions look like.</summary>
public sealed class NotificationTemplateSummaryRow
{
    public required Guid TemplateId { get; init; }

    public required string Type { get; init; }

    public required NotificationCategory Category { get; init; }

    public required NotificationChannel Channel { get; init; }

    public required string Language { get; init; }

    public required string Name { get; init; }

    public Guid? PublishedVersionId { get; init; }

    public int? PublishedVersionNumber { get; init; }

    public DateTime? PublishedAtUtc { get; init; }

    public bool HasDraft { get; init; }
}

/// <summary>A version together with the type and language of the template it belongs to.</summary>
public sealed record NotificationTemplateVersionLookup(NotificationTemplateVersion Version, string Type, string Language);

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

    /// <summary>The admin list, ordered by type, channel and language.</summary>
    Task<IReadOnlyList<NotificationTemplateSummaryRow>> ListAsync(NotificationTemplateFilter filter, CancellationToken cancellationToken);

    /// <summary>The template with all of its versions; tracked when <paramref name="track"/> so the caller can change it.</summary>
    Task<NotificationTemplate?> GetWithVersionsAsync(Guid templateId, bool track, CancellationToken cancellationToken);

    /// <summary>One version in any status, for rendering a test send; null when it doesn't exist.</summary>
    Task<NotificationTemplateVersionLookup?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken);

    /// <summary>Makes the next save fail with a concurrency conflict unless the row still has <paramref name="rowVersion"/>.</summary>
    void ExpectRowVersion(NotificationTemplateVersion version, byte[] rowVersion);

    void Add(NotificationTemplate notificationTemplate);
}
