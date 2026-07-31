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
/// Cached/reasoning token counts, latency, estimated cost, and comparison-context fields
/// were added for SPEC-005 (specs/005-multi-provider-ai-engine, FR-020/FR-025) — Provider/
/// Model stay free-text (not FKs to AIProvider/AIModel) so this immutable history survives a
/// provider/model being disabled, deprecated, or removed from the catalog entirely (FR-011).
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

    /// <summary>Populated only when the provider reports it (FR-020, specs/005-multi-provider-ai-engine).</summary>
    public int? CachedTokenCount { get; private set; }

    /// <summary>Populated only for reasoning-capable models (FR-020).</summary>
    public int? ReasoningTokenCount { get; private set; }

    public int? LatencyMs { get; private set; }

    /// <summary>Null (not zero) when pricing is unavailable (FR-022) — never a fabricated value.</summary>
    public decimal? EstimatedCostUsd { get; private set; }

    /// <summary>Non-null only for assistant messages produced by a model-comparison call (User Story 7) — groups the N candidate responses to one comparison.</summary>
    public Guid? ComparisonGroupId { get; private set; }

    /// <summary>
    /// Whether this message is fed back as context on the next send. True for every ordinary
    /// message; for comparison candidates (FR-025), true only for the one the user chose to
    /// continue from — decided once at creation, never flipped afterward (this entity stays
    /// append-only/immutable).
    /// </summary>
    public bool IsIncludedInContext { get; private set; } = true;

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
        int? outputTokenCount = null,
        int? cachedTokenCount = null,
        int? reasoningTokenCount = null,
        int? latencyMs = null,
        decimal? estimatedCostUsd = null,
        Guid? comparisonGroupId = null,
        bool isIncludedInContext = true)
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
            CachedTokenCount = cachedTokenCount,
            ReasoningTokenCount = reasoningTokenCount,
            LatencyMs = latencyMs,
            EstimatedCostUsd = estimatedCostUsd,
            ComparisonGroupId = comparisonGroupId,
            IsIncludedInContext = isIncludedInContext,
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
