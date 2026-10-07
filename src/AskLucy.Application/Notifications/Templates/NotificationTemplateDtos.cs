using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Templates;

public sealed record TemplatePublishedVersionDto(Guid Id, int VersionNumber, DateTime? PublishedAtUtc);

public sealed record NotificationTemplateSummaryDto(
    Guid TemplateId,
    string Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    string Language,
    string Name,
    TemplatePublishedVersionDto? PublishedVersion,
    bool HasDraft);

public sealed record TemplateVariableDto(string Name, string Sample, string Fallback, bool IsStandard);

public sealed record TemplateVersionSummaryDto(
    Guid Id,
    int VersionNumber,
    TemplateVersionStatus Status,
    DateTime CreatedAtUtc,
    string? CreatedBy,
    DateTime? PublishedAtUtc,
    DateTime? ArchivedAtUtc);

public sealed record NotificationTemplateDetailDto(
    Guid TemplateId,
    string Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    string Language,
    string Name,
    Guid? PublishedVersionId,
    bool IsShippedDefault,
    IReadOnlyList<TemplateVersionSummaryDto> Versions,
    IReadOnlyList<TemplateVariableDto> DeclaredVariables);

/// <summary>One version's editable fields; the email fields are null for an in-app template and the other way round.</summary>
public sealed record TemplateVersionDto(
    Guid Id,
    Guid TemplateId,
    int VersionNumber,
    TemplateVersionStatus Status,
    string RowVersion,
    string? Subject,
    string? Preheader,
    string? Greeting,
    string? Heading,
    IReadOnlyList<string> BodyParagraphs,
    string? ActionLabel,
    string? SafetyNote,
    string? FooterNote,
    string? Title,
    string? Message,
    IReadOnlyList<string> UsedVariables,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? ArchivedAtUtc);

/// <summary>The preview of a version, rendered by the production renderer. The HTML is for a sandboxed frame only.</summary>
public sealed record TemplatePreviewDto(
    string? Subject, string? Html, string? Text, string? Title, string? Message, string? ActionLabel, string Language, string Direction);

public sealed record SendTemplateTestResult(string SentTo);
