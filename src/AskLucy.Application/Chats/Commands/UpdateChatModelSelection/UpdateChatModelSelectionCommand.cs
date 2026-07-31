using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Chats.Commands.UpdateChatModelSelection;

/// <summary>contracts/chat.md `PATCH /api/v1/chats/{chatId}/model-selection` — applies to messages sent after this call only (FR-009); prior messages keep their original attribution (FR-011).</summary>
public sealed record UpdateChatModelSelectionCommand(
    Guid ChatId, Guid ProviderId, Guid ModelId, GenerationParametersDto? GenerationParameters) : IRequest;
