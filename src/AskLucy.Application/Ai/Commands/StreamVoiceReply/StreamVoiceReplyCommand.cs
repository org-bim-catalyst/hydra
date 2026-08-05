using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.StreamVoiceReply;

/// <summary>contracts/voice-reply-stream.md `POST /api/v1/ai/voice/reply`. Mirrors
/// <see cref="Commands.SendChatMessage.SendChatMessageCommand"/>'s shape plus
/// <paramref name="Language"/> (research.md Decision 9) — persistence (user/assistant
/// <c>Message</c> rows) stays a controller concern, exactly as it does for
/// <c>AiController.Chat</c>, not something this command does itself. <see cref="ChatId"/>
/// (specs/016-rag-semantic-search US1) is threaded straight through to
/// <see cref="Commands.SendChatMessage.SendChatMessageCommand"/> so a voice reply is grounded by
/// the conversation's attached knowledge bases exactly like a typed chat message — the RAG
/// retrieval outcome itself isn't surfaced through <see cref="VoiceReplyEvent"/> (out of this
/// user story's scope), only the augmented prompt benefits.</summary>
public sealed record StreamVoiceReplyCommand(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    string Language) : IStreamRequest<VoiceReplyEvent>;
