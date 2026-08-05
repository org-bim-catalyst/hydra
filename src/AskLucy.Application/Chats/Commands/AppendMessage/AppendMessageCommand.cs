using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.AppendMessage;

/// <summary>A file to attach to the message being appended (FR-017).</summary>
public sealed record AppendMessageAttachmentInput(string FileName, string ContentType, string AccessLocation);

/// <summary>
/// A source citation to attach to the message being appended (FR-017). The trailing RAG-specific
/// fields (research.md Decision 9, spec.md FR-030) are populated only for a RAG-grounded citation
/// (specs/016-rag-semantic-search US1) — when <see cref="DocumentChunkId"/> is set, the handler
/// calls <c>Message.AddCitationFromChunk</c> instead of the plain <c>Message.AddCitation</c>.
/// </summary>
public sealed record AppendMessageCitationInput(
    string SourceLabel, string? SourceReference,
    Guid? DocumentChunkId = null, Guid? KnowledgeBaseId = null, Guid? DocumentId = null,
    Guid? DocumentVersionId = null, int? PageNumber = null, string? Section = null);

/// <summary>
/// Persists one turn of a chat's history (2026-07-28 decision to add ChatGPT-style
/// persisted conversation history — supersedes spec.md FR-026 for this capability; see
/// spec.md Clarifications). Deliberately separate from the AI commands themselves
/// (SendChatMessageCommand/TranslateCommand/GenerateImageCommand) so persistence is an
/// orchestration concern the controller composes, not a change to those commands' existing,
/// already-tested behavior.
/// Extended for specs/002-chat-history-management (FR-016/FR-017) with provider/model/
/// token/generation-parameter metadata and optional attachments/citations — all optional so
/// existing callers (user-message appends, which have none of this) are unaffected. Further
/// extended for specs/005-multi-provider-ai-engine (FR-020) with cached/reasoning token
/// counts, latency, and estimated cost — same optionality rule.
/// </summary>
public sealed record AppendMessageCommand(
    Guid ChatId,
    MessageRole Role,
    MessageKind Kind,
    string Content,
    string? SourceText,
    string? Provider = null,
    string? Model = null,
    string? GenerationParametersJson = null,
    int? InputTokenCount = null,
    int? OutputTokenCount = null,
    int? CachedTokenCount = null,
    int? ReasoningTokenCount = null,
    int? LatencyMs = null,
    decimal? EstimatedCostUsd = null,
    IReadOnlyList<AppendMessageAttachmentInput>? Attachments = null,
    IReadOnlyList<AppendMessageCitationInput>? Citations = null) : IRequest<MessageDto>;
