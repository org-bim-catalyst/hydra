using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.AppendMessage;

/// <summary>A file to attach to the message being appended (FR-017).</summary>
public sealed record AppendMessageAttachmentInput(string FileName, string ContentType, string AccessLocation);

/// <summary>A source citation to attach to the message being appended (FR-017).</summary>
public sealed record AppendMessageCitationInput(string SourceLabel, string? SourceReference);

/// <summary>
/// Persists one turn of a chat's history (2026-07-28 decision to add ChatGPT-style
/// persisted conversation history — supersedes spec.md FR-026 for this capability; see
/// spec.md Clarifications). Deliberately separate from the AI commands themselves
/// (SendChatMessageCommand/TranslateCommand/GenerateImageCommand) so persistence is an
/// orchestration concern the controller composes, not a change to those commands' existing,
/// already-tested behavior.
/// Extended for specs/002-chat-history-management (FR-016/FR-017) with provider/model/
/// token/generation-parameter metadata and optional attachments/citations — all optional so
/// existing callers (user-message appends, which have none of this) are unaffected.
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
    IReadOnlyList<AppendMessageAttachmentInput>? Attachments = null,
    IReadOnlyList<AppendMessageCitationInput>? Citations = null) : IRequest<MessageDto>;
