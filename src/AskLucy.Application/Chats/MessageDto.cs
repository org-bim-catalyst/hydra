namespace AskLucy.Application.Chats;

public sealed record MessageDto(Guid Id, string Role, string Kind, string Content, string? SourceText, DateTime CreatedAtUtc);
