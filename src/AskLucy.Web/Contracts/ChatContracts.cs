using AskLucy.Application.Ai;

namespace AskLucy.Web.Contracts;

public sealed record CreateChatRequest(string Title, string? SessionId);

public sealed record RenameChatRequest(string Title);

/// <summary>contracts/chat.md `PATCH /api/v1/chats/{chatId}/model-selection`.</summary>
public sealed record UpdateChatModelSelectionRequest(Guid ProviderId, Guid ModelId, GenerationParametersDto? GenerationParameters);

/// <summary>Body for Clear Messages / Permanent Delete (FR-004/FR-011) — confirmation is enforced again at the Application boundary, not only here.</summary>
public sealed record ConfirmActionRequest(bool Confirm);
