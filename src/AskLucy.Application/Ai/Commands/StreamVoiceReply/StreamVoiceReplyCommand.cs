using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.StreamVoiceReply;

/// <summary>contracts/voice-reply-stream.md `POST /api/v1/ai/voice/reply`. Mirrors
/// <see cref="Commands.SendChatMessage.SendChatMessageCommand"/>'s shape plus
/// <paramref name="Language"/> (research.md Decision 9) — persistence (user/assistant
/// <c>Message</c> rows) stays a controller concern, exactly as it does for
/// <c>AiController.Chat</c>, not something this command does itself.</summary>
public sealed record StreamVoiceReplyCommand(
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    string Language) : IStreamRequest<VoiceReplyEvent>;
