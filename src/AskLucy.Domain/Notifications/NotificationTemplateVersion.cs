using System.Text.Json;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// The editable fields of one template version (research R9). Email versions use the structured
/// email fields, in-app versions use <see cref="Title"/> and <see cref="Message"/>; both may have
/// an <see cref="ActionLabel"/>. There is no free HTML anywhere.
/// </summary>
public sealed record NotificationTemplateContent
{
    public string? Subject { get; init; }

    public string? Preheader { get; init; }

    public string? Greeting { get; init; }

    public string? Heading { get; init; }

    public IReadOnlyList<string> BodyParagraphs { get; init; } = [];

    public string? SafetyNote { get; init; }

    public string? FooterNote { get; init; }

    public string? Title { get; init; }

    public string? Message { get; init; }

    public string? ActionLabel { get; init; }
}

/// <summary>A revision of a <see cref="NotificationTemplate"/>; immutable once published (FR-039).</summary>
public sealed class NotificationTemplateVersion : BaseEntity
{
    public const int SubjectMaxLength = 200;
    public const int PreheaderMaxLength = 200;
    public const int GreetingMaxLength = 200;
    public const int HeadingMaxLength = 200;
    public const int ParagraphMaxLength = 1000;
    public const int MaxParagraphs = 10;
    public const int SafetyNoteMaxLength = 500;
    public const int FooterNoteMaxLength = 500;
    public const int TitleMaxLength = 200;
    public const int MessageMaxLength = 1000;
    public const int ActionLabelMaxLength = 60;

    public Guid TemplateId { get; private set; }

    public int VersionNumber { get; private set; }

    public TemplateVersionStatus Status { get; private set; }

    public string? Subject { get; private set; }

    public string? Preheader { get; private set; }

    public string? Greeting { get; private set; }

    public string? Heading { get; private set; }

    public string? BodyParagraphsJson { get; private set; }

    public string? SafetyNote { get; private set; }

    public string? FooterNote { get; private set; }

    public string? Title { get; private set; }

    public string? Message { get; private set; }

    public string? ActionLabel { get; private set; }

    /// <summary>The variables the fields reference, computed on save (FR-041).</summary>
    public string UsedVariablesJson { get; private set; } = "[]";

    public DateTime? PublishedAtUtc { get; private set; }

    public string? PublishedBy { get; private set; }

    public DateTime? ArchivedAtUtc { get; private set; }

    public string? ArchivedBy { get; private set; }

    public IReadOnlyList<string> BodyParagraphs =>
        string.IsNullOrEmpty(BodyParagraphsJson) ? [] : JsonSerializer.Deserialize<string[]>(BodyParagraphsJson) ?? [];

    public IReadOnlyList<string> UsedVariables => JsonSerializer.Deserialize<string[]>(UsedVariablesJson) ?? [];

    public NotificationTemplateContent Content => new()
    {
        Subject = Subject,
        Preheader = Preheader,
        Greeting = Greeting,
        Heading = Heading,
        BodyParagraphs = BodyParagraphs,
        SafetyNote = SafetyNote,
        FooterNote = FooterNote,
        Title = Title,
        Message = Message,
        ActionLabel = ActionLabel,
    };

    private NotificationTemplateVersion()
    {
        // Required by EF Core materialization.
    }

    internal static NotificationTemplateVersion CreateDraft(
        Guid templateId,
        int versionNumber,
        NotificationChannel channel,
        NotificationTypeDefinition definition,
        NotificationTemplateContent content,
        DateTime now)
    {
        var version = new NotificationTemplateVersion
        {
            Id = Guid.CreateVersion7(),
            TemplateId = templateId,
            VersionNumber = versionNumber,
            Status = TemplateVersionStatus.Draft,
            CreatedAtUtc = now,
        };
        version.Apply(channel, definition, content);
        return version;
    }

    internal void UpdateDraft(NotificationChannel channel, NotificationTypeDefinition definition, NotificationTemplateContent content)
    {
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new DomainRuleViolationException("Only a draft template version can be edited.");
        }

        Apply(channel, definition, content);
    }

    internal void MarkPublished(string userId, DateTime now)
    {
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new DomainRuleViolationException($"A {Status} template version can't be published.");
        }

        Status = TemplateVersionStatus.Published;
        PublishedAtUtc = now;
        PublishedBy = userId;
    }

    internal void MarkArchived(string userId, DateTime now)
    {
        if (Status == TemplateVersionStatus.Archived)
        {
            throw new DomainRuleViolationException("The template version is already archived.");
        }

        Status = TemplateVersionStatus.Archived;
        ArchivedAtUtc = now;
        ArchivedBy = userId;
    }

    /// <summary>Validates every field for the channel and the type's variables, then stores it (FR-041, FR-047, FR-050).</summary>
    private void Apply(NotificationChannel channel, NotificationTypeDefinition definition, NotificationTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(content);

        var errors = new List<string>();
        var used = new List<string>();

        bool IsDeclared(string name) => definition.VariablesFromTemplate || definition.Declares(name);

        void Check(string field, string? value, int maxLength, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    errors.Add($"{field} is required.");
                }

                return;
            }

            if (value.Length > maxLength)
            {
                errors.Add($"{field} can't exceed {maxLength} characters.");
            }

            var result = TemplateTokenParser.ValidateAgainst(value, IsDeclared);
            errors.AddRange(result.Errors.Select(e => $"{field}: {e.Message}"));
            used.AddRange(result.Variables.Where(v => !used.Contains(v, StringComparer.Ordinal)));
        }

        void Forbid(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field} isn't used by {channel} templates.");
            }
        }

        var isEmail = channel == NotificationChannel.Email;
        if (isEmail)
        {
            Check(nameof(content.Subject), content.Subject, SubjectMaxLength, required: true);
            Check(nameof(content.Preheader), content.Preheader, PreheaderMaxLength, required: false);
            Check(nameof(content.Greeting), content.Greeting, GreetingMaxLength, required: false);
            Check(nameof(content.Heading), content.Heading, HeadingMaxLength, required: true);
            Check(nameof(content.SafetyNote), content.SafetyNote, SafetyNoteMaxLength, required: true);
            Check(nameof(content.FooterNote), content.FooterNote, FooterNoteMaxLength, required: false);
            if (content.BodyParagraphs.Count is < 1 or > MaxParagraphs)
            {
                errors.Add($"An email needs between 1 and {MaxParagraphs} body paragraphs.");
            }

            for (var i = 0; i < content.BodyParagraphs.Count; i++)
            {
                Check($"Paragraph {i + 1}", content.BodyParagraphs[i], ParagraphMaxLength, required: true);
            }

            Forbid(nameof(content.Title), content.Title);
            Forbid(nameof(content.Message), content.Message);
        }
        else
        {
            Check(nameof(content.Title), content.Title, TitleMaxLength, required: true);
            Check(nameof(content.Message), content.Message, MessageMaxLength, required: true);
            Forbid(nameof(content.Subject), content.Subject);
            Forbid(nameof(content.Preheader), content.Preheader);
            Forbid(nameof(content.Greeting), content.Greeting);
            Forbid(nameof(content.Heading), content.Heading);
            Forbid(nameof(content.SafetyNote), content.SafetyNote);
            Forbid(nameof(content.FooterNote), content.FooterNote);
            if (content.BodyParagraphs.Count > 0)
            {
                errors.Add($"Body paragraphs aren't used by {channel} templates.");
            }
        }

        Check(nameof(content.ActionLabel), content.ActionLabel, ActionLabelMaxLength, required: false);

        if (errors.Count > 0)
        {
            throw new DomainRuleViolationException(string.Join(" ", errors));
        }

        Subject = Normalize(content.Subject);
        Preheader = Normalize(content.Preheader);
        Greeting = Normalize(content.Greeting);
        Heading = Normalize(content.Heading);
        BodyParagraphsJson = isEmail ? JsonSerializer.Serialize(content.BodyParagraphs.Select(p => p.Trim()).ToArray()) : null;
        SafetyNote = Normalize(content.SafetyNote);
        FooterNote = Normalize(content.FooterNote);
        Title = Normalize(content.Title);
        Message = Normalize(content.Message);
        ActionLabel = Normalize(content.ActionLabel);
        UsedVariablesJson = JsonSerializer.Serialize(used);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
