using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// Streams a chat completion (FR-001) via SSE (research.md Topic 2). Uses MediatR's
/// stream-request support so the handler can yield tokens as they arrive from
/// <see cref="Abstractions.IAIProvider.StreamChatAsync"/>.
/// </summary>
public sealed record SendChatMessageCommand(IReadOnlyList<ChatMessageDto> Messages) : IStreamRequest<string>;
