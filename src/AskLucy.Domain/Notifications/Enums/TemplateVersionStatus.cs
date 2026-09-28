namespace AskLucy.Domain.Notifications;

/// <summary>Template version lifecycle (FR-039). Published versions are immutable.</summary>
public enum TemplateVersionStatus
{
    Draft,
    Published,
    Archived,
}
