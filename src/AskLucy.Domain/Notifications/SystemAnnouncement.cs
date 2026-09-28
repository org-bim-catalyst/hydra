using System.Text.Json;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// An administrator's announcement to active users (FR-004a). Immutable once published: a
/// correction is a new announcement. It is never a marketing channel: email goes out only when it
/// is marked critical, and only to users who kept System email on.
/// </summary>
public sealed class SystemAnnouncement : BaseEntity
{
    public const int TitleMaxLength = 150;
    public const int MessageMaxLength = 2000;

    public AnnouncementKind Kind { get; private set; }

    /// <summary>Administrator-entered; never translated (FR-046b).</summary>
    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public AnnouncementAudience Audience { get; private set; }

    public string? TargetRoleIdsJson { get; private set; }

    public bool IsCritical { get; private set; }

    public DateTime? EndsAtUtc { get; private set; }

    public DateTime PublishedAtUtc { get; private set; }

    public string PublishedByUserId { get; private set; } = string.Empty;

    /// <summary>Set when the fan-out completes (R22).</summary>
    public int? RecipientCount { get; private set; }

    public IReadOnlyList<string> TargetRoleIds =>
        TargetRoleIdsJson is null ? [] : JsonSerializer.Deserialize<string[]>(TargetRoleIdsJson) ?? [];

    private SystemAnnouncement()
    {
        // Required by EF Core materialization.
    }

    public static SystemAnnouncement Publish(
        AnnouncementKind kind,
        string title,
        string message,
        AnnouncementAudience audience,
        IReadOnlyCollection<string>? targetRoleIds,
        bool isCritical,
        DateTime? endsAtUtc,
        string publishedByUserId,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedByUserId);

        var trimmedTitle = title?.Trim() ?? string.Empty;
        var trimmedMessage = message?.Trim() ?? string.Empty;
        if (trimmedTitle.Length is 0 or > TitleMaxLength)
        {
            throw new DomainRuleViolationException($"An announcement needs a title of at most {TitleMaxLength} characters.");
        }

        if (trimmedMessage.Length is 0 or > MessageMaxLength)
        {
            throw new DomainRuleViolationException($"An announcement needs a message of at most {MessageMaxLength} characters.");
        }

        if (TemplateTokenParser.ContainsRawUrlOrHtml(trimmedTitle) || TemplateTokenParser.ContainsRawUrlOrHtml(trimmedMessage))
        {
            throw new DomainRuleViolationException("Links and HTML aren't allowed in an announcement.");
        }

        var roles = targetRoleIds?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (audience == AnnouncementAudience.Roles && roles.Length == 0)
        {
            throw new DomainRuleViolationException("Choose at least one role for a role-targeted announcement.");
        }

        if (audience == AnnouncementAudience.AllActiveUsers && roles.Length > 0)
        {
            throw new DomainRuleViolationException("An announcement to all active users can't also target roles.");
        }

        if (endsAtUtc is { } ends && ends <= now)
        {
            throw new DomainRuleViolationException("The announcement end time must be in the future.");
        }

        return new SystemAnnouncement
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Title = trimmedTitle,
            Message = trimmedMessage,
            Audience = audience,
            TargetRoleIdsJson = roles.Length == 0 ? null : JsonSerializer.Serialize(roles),
            IsCritical = isCritical,
            EndsAtUtc = endsAtUtc,
            PublishedAtUtc = now,
            PublishedByUserId = publishedByUserId,
            CreatedAtUtc = now,
            CreatedBy = publishedByUserId,
        };
    }

    public void RecordFanOutCompleted(int recipientCount)
    {
        if (recipientCount < 0)
        {
            throw new DomainRuleViolationException("A recipient count can't be negative.");
        }

        RecipientCount = recipientCount;
    }
}
