namespace AskLucy.Application.Notifications.Templates;

/// <summary>Why a template action was refused with 409 (contracts/admin-notifications-api.md); surfaced as the <c>reason</c> extension.</summary>
public enum TemplateConflictReason
{
    VersionNotDraft,
    VersionArchived,
    ConcurrencyConflict,
    LastPublishedDefault,
}

/// <summary>A template action conflicts with the current state of the template. Mapped to 409 with <see cref="Reason"/>.</summary>
public sealed class NotificationTemplateConflictException(TemplateConflictReason reason, string message) : Exception(message)
{
    public TemplateConflictReason Reason { get; } = reason;
}

/// <summary>The template content (or a test-send) can't be accepted: an unknown variable, a malformed token, raw HTML, a length limit. Mapped to 422.</summary>
public sealed class NotificationTemplateRejectedException(string message) : Exception(message);
