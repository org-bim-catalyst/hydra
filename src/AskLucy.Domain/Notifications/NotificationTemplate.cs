using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// One editable template per <c>(Type, Channel, Language)</c> (FR-038; research R9). Aggregate root
/// of its <see cref="NotificationTemplateVersion"/> rows.
/// </summary>
public sealed class NotificationTemplate : BaseEntity
{
    public const int NameMaxLength = 150;
    public const int LanguageMaxLength = 10;

    private readonly List<NotificationTemplateVersion> _versions = [];

    public string Type { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; }

    public string Language { get; private set; } = "en";

    public string Name { get; private set; } = string.Empty;

    /// <summary>Copied from the type; read-only.</summary>
    public NotificationCategory Category { get; private set; }

    public Guid? PublishedVersionId { get; private set; }

    public IReadOnlyCollection<NotificationTemplateVersion> Versions => _versions.AsReadOnly();

    public NotificationTemplateVersion? PublishedVersion =>
        _versions.SingleOrDefault(v => v.Status == TemplateVersionStatus.Published);

    /// <summary>
    /// A template the platform ships for an emitted type. SC-006 needs one published version for it
    /// at all times, so its last published version can't be archived.
    /// </summary>
    public bool IsShippedDefault =>
        NotificationTypeCatalog.TryGet(Type, out var definition) && definition!.IsEmitted;

    private NotificationTemplate()
    {
        // Required by EF Core materialization.
    }

    public static NotificationTemplate Create(string type, NotificationChannel channel, string language, string name, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!NotificationTypeCatalog.TryGet(type, out var definition))
        {
            throw new DomainRuleViolationException($"'{type}' is not a notification type.");
        }

        if (!definition!.Supports(channel))
        {
            throw new DomainRuleViolationException($"'{type}' notifications aren't sent by {channel}.");
        }

        if (language.Length > LanguageMaxLength || name.Length > NameMaxLength)
        {
            throw new DomainRuleViolationException("The template language or name is too long.");
        }

        return new NotificationTemplate
        {
            Id = Guid.CreateVersion7(),
            Type = definition.Key,
            Channel = channel,
            Language = language.Trim(),
            Name = name.Trim(),
            Category = definition.Category,
            CreatedAtUtc = now,
        };
    }

    public NotificationTypeDefinition Definition => NotificationTypeCatalog.Get(Type);

    /// <summary>Adds a new draft, numbered after the highest existing version.</summary>
    public NotificationTemplateVersion AddDraft(NotificationTemplateContent content, DateTime now)
    {
        var next = _versions.Count == 0 ? 1 : _versions.Max(v => v.VersionNumber) + 1;
        var version = NotificationTemplateVersion.CreateDraft(Id, next, Channel, Definition, content, now);
        _versions.Add(version);
        return version;
    }

    public void UpdateDraft(Guid versionId, NotificationTemplateContent content) =>
        Find(versionId).UpdateDraft(Channel, Definition, content);

    /// <summary>Publishes a draft and archives the version it replaces (FR-039).</summary>
    public void Publish(Guid versionId, string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var version = Find(versionId);
        var previous = PublishedVersion;

        version.MarkPublished(userId, now);
        previous?.MarkArchived(userId, now);
        PublishedVersionId = version.Id;
    }

    /// <summary>
    /// Archives a draft or the published version. Refused for the last published version of a
    /// shipped default (SC-006).
    /// </summary>
    public void Archive(Guid versionId, string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var version = Find(versionId);
        if (version.Status == TemplateVersionStatus.Published && IsShippedDefault)
        {
            throw new DomainRuleViolationException("This template must always have a published version. Publish a replacement instead.");
        }

        version.MarkArchived(userId, now);
        if (PublishedVersionId == version.Id)
        {
            PublishedVersionId = null;
        }
    }

    private NotificationTemplateVersion Find(Guid versionId) =>
        _versions.SingleOrDefault(v => v.Id == versionId)
        ?? throw new DomainRuleViolationException("The version doesn't belong to this template.");
}
