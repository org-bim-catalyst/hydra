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
/// </summary>
public sealed class Message : BaseEntity
{
    public Guid UserChatId { get; private set; }

    public MessageRole Role { get; private set; }

    public MessageKind Kind { get; private set; }

    /// <summary>The rendered content: chat reply text, the generated image's URL, or the translated text.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>The original prompt behind an Image/Translation-kind assistant message; null for plain Text messages.</summary>
    public string? SourceText { get; private set; }

    private Message()
    {
        // Required by EF Core materialization.
    }

    public static Message Create(
        Guid userChatId, MessageRole role, MessageKind kind, string content, string? sourceText, string actor)
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
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
