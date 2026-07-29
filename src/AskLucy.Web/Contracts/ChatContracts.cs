namespace AskLucy.Web.Contracts;

public sealed record CreateChatRequest(string Title, string? SessionId);

public sealed record RenameChatRequest(string Title);
