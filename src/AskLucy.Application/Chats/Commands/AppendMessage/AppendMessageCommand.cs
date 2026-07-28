using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.AppendMessage;

/// <summary>
/// Persists one turn of a chat's history (2026-07-28 decision to add ChatGPT-style
/// persisted conversation history — supersedes spec.md FR-026 for this capability; see
/// spec.md Clarifications). Deliberately separate from the AI commands themselves
/// (SendChatMessageCommand/TranslateCommand/GenerateImageCommand) so persistence is an
/// orchestration concern the controller composes, not a change to those commands' existing,
/// already-tested behavior.
/// </summary>
public sealed record AppendMessageCommand(
    Guid ChatId, MessageRole Role, MessageKind Kind, string Content, string? SourceText) : IRequest<MessageDto>;
