namespace AskLucy.Web.Contracts;

public sealed record CreateChatRequest(string Title, string? SessionId);

public sealed record RenameChatRequest(string Title);

/// <summary>Body for Clear Messages / Permanent Delete (FR-004/FR-011) — confirmation is enforced again at the Application boundary, not only here.</summary>
public sealed record ConfirmActionRequest(bool Confirm);
