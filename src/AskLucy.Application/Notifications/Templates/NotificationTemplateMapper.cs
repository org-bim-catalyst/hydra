using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Templates;

internal static class NotificationTemplateMapper
{
    public static TemplateVersionDto ToDto(NotificationTemplateVersion v) => new(
        v.Id,
        v.TemplateId,
        v.VersionNumber,
        v.Status,
        Convert.ToBase64String(v.RowVersion),
        v.Subject,
        v.Preheader,
        v.Greeting,
        v.Heading,
        v.BodyParagraphs,
        v.ActionLabel,
        v.SafetyNote,
        v.FooterNote,
        v.Title,
        v.Message,
        v.UsedVariables,
        v.CreatedAtUtc,
        v.PublishedAtUtc,
        v.ArchivedAtUtc);

    public static NotificationTemplateVersion FindVersion(NotificationTemplate template, Guid versionId) =>
        template.Versions.SingleOrDefault(v => v.Id == versionId)
        ?? throw new KeyNotFoundException("Template version not found.");

    /// <summary>The draft editor's concurrency check (FR-039): the caller must hold the row version it last read.</summary>
    public static void EnsureCurrent(NotificationTemplateVersion version, byte[] expectedRowVersion)
    {
        if (!version.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            throw new NotificationTemplateConflictException(
                TemplateConflictReason.ConcurrencyConflict, "This template version was changed by someone else. Reload it and try again.");
        }
    }

    public static void EnsureDraft(NotificationTemplateVersion version)
    {
        if (version.Status == TemplateVersionStatus.Draft)
        {
            return;
        }

        throw new NotificationTemplateConflictException(
            version.Status == TemplateVersionStatus.Archived ? TemplateConflictReason.VersionArchived : TemplateConflictReason.VersionNotDraft,
            $"Only a draft can be changed; this version is {version.Status}. Create a new version instead.");
    }
}
