using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// Streams a chat completion, resolved to a specific provider/model
/// (specs/005-multi-provider-ai-engine contracts/chat.md). Yields <see cref="StreamChunk"/>
/// rather than a plain string so the final usage/cost can ride the last chunk without
/// changing the SSE wire format the controller writes (only content deltas get written out).
/// </summary>
public sealed record SendChatMessageCommand(
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters) : IStreamRequest<StreamChunk>;
