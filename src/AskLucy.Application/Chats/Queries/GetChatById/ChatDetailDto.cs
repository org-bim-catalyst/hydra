using AskLucy.Domain.Chats;

namespace AskLucy.Application.Chats.Queries.GetChatById;

/// <summary>
/// specs/025-chat-configuration-settings, contracts/chat-detail-api.md — a single chat's
/// current provider/model selection, previously persisted but never queryable. Null
/// <see cref="ProviderId"/>/<see cref="ModelId"/> means the conversation has never had a
/// model selection saved yet (e.g. before its first message).
/// </summary>
public sealed record ChatDetailDto(Guid Id, string Title, Guid? ProviderId, Guid? ModelId)
{
    public static ChatDetailDto FromEntity(UserChat chat) => new(chat.Id, chat.Title, chat.ProviderId, chat.ModelId);
}
