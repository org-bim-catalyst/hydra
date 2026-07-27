namespace AskLucy.Application.Chats;

public sealed record UserChatDto(Guid Id, string Title, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc);
