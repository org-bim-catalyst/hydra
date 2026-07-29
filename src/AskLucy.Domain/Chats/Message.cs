using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

public enum MessageRole
{
    User,
    Assistant,
}

/// <summary>Determines how <see cref="Message.Content"/> is rendered — plain chat text, an image URL, or a translated string.</summary>
public enum MessageKind
{
    Text,
    Image,
    Translation,
}

/// <summary>
/// A single turn in a <see cref="UserChat"/>'s persisted conversation history. Added to
/// mimic a ChatGPT-style history/reload experience (2026-07-28 decision) — this
/// intentionally supersedes spec.md FR-026's original "no persisted message/conversation-
/// content table" constraint for this one capability; see spec.md's Clarifications for the
/// override record. Messages are immutable once created (append-only) — no rename, no edit.
/// Provider/model/token/generation-parameter metadata and Attachments/Citations were added
/// for SPEC-002 (specs/002-chat-history-management, FR-016/FR-017); Attachments/Citations
/// are children of this aggregate, not independently reachable (constitution &#167;5).
/// </summary>
public sealed class Message : BaseEntity
{
    private readonly List<Attachment> _attachments = [];
    private readonly List<Citation> _citations = [];

    public Guid UserChatId { get; private set; }

    public MessageRole Role { get; private set; }

    public MessageKind Kind { get; private set; }

    /// <summary>The rendered content: chat reply text, the generated image's URL, or the translated text.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>The original prompt behind an Image/Translation-kind assistant message; null for plain Text messages.</summary>
    public string? SourceText { get; private set; }

    /// <summary>The AI provider that produced this message (assistant messages only); null for user messages.</summary>
    public string? Provider { get; private set; }

    /// <summary>The model identifier that produced this message (assistant messages only); null for user messages.</summary>
    public string? Model { get; private set; }

    /// <summary>Serialized generation parameters (temperature, etc.) in effect when this message was produced; provider/model-shape varies, so stored as opaque JSON rather than a fixed value object (research.md Topic 2/data-model.md).</summary>
    public string? GenerationParametersJson { get; private set; }

    public int? InputTokenCount { get; private set; }

    public int? OutputTokenCount { get; private set; }

    public IReadOnlyCollection<Attachment> Attachments => _attachments;

    public IReadOnlyCollection<Citation> Citations => _citations;

    private Message()
    {
        // Required by EF Core materialization.
    }

    public static Message Create(
        Guid userChatId,
        MessageRole role,
        MessageKind kind,
        string content,
        string? sourceText,
        string actor,
        string? provider = null,
        string? model = null,
        string? generationParametersJson = null,
        int? inputTokenCount = null,
        int? outputTokenCount = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleViolationException("Message content is required.");
        }

        return new Message
        {
            Id = Guid.CreateVersion7(),
            UserChatId = userChatId,
            Role = role,
            Kind = kind,
            Content = content,
            SourceText = sourceText,
            Provider = provider,
            Model = model,
            GenerationParametersJson = generationParametersJson,
            InputTokenCount = inputTokenCount,
            OutputTokenCount = outputTokenCount,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public Attachment AddAttachment(string fileName, string contentType, string accessLocation, string actor)
    {
        var attachment = Attachment.Create(Id, fileName, contentType, accessLocation, actor);
        _attachments.Add(attachment);
        return attachment;
    }

    public Citation AddCitation(string sourceLabel, string? sourceReference, string actor)
    {
        var citation = Citation.Create(Id, sourceLabel, sourceReference, actor);
        _citations.Add(citation);
        return citation;
    }
}
