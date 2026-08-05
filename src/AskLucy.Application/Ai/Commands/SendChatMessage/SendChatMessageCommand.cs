using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// Streams a chat completion, resolved to a specific provider/model
/// (specs/005-multi-provider-ai-engine contracts/chat.md). Yields <see cref="ChatStreamChunk"/>
/// rather than a plain string so the final usage/cost — and, since US1
/// (specs/016-rag-semantic-search), the RAG retrieval outcome — can ride the last chunk without
/// changing the SSE wire format the controller writes (only content deltas get written out as
/// plain text; RAG metadata is written as one distinguishable trailing JSON event).
/// <see cref="ChatId"/> (added for US1) is used to look up the conversation's attached knowledge
/// bases — retrieval only runs when at least one is attached (research.md Decision 8).
/// </summary>
public sealed record SendChatMessageCommand(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters) : IStreamRequest<ChatStreamChunk>;
