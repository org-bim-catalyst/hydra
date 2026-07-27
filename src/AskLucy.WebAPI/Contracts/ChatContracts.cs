namespace AskLucy.WebAPI.Contracts;

public sealed record CreateChatRequest(string Title, string? SessionId);

public sealed record RenameChatRequest(string Title);
