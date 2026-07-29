namespace AskLucy.Application.Chats.Queries.SearchUserChats;

/// <summary>Which slice of a user's conversations to list (FR-020) — `view` query parameter (contracts/chats-api.md).</summary>
public enum ConversationView
{
    /// <summary>Not archived, not deleted — the default view.</summary>
    Active,

    Archived,

    /// <summary>"Recently Deleted" (Trash) — bypasses the soft-delete query filter (research.md Topic 2).</summary>
    Deleted,

    /// <summary>Every conversation regardless of archived/deleted state.</summary>
    All,
}

/// <summary>Conversation list sort order (FR-021) — `sort` query parameter. Pinned conversations always sort ahead of unpinned ones regardless of this choice (FR-008).</summary>
public enum ConversationSort
{
    Newest,
    Oldest,
    RecentlyUpdated,
    Alphabetical,
}
