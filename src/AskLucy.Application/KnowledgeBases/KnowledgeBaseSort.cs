namespace AskLucy.Application.KnowledgeBases;

/// <summary>Knowledge base list sort order (FR-024) — `sort` query parameter. Pinned knowledge bases always sort ahead of unpinned ones regardless of this choice (FR-028), mirroring `ConversationSort`'s pinned-first convention.</summary>
public enum KnowledgeBaseSort
{
    Name,
    RecentlyUpdated,
    Created,
    DocumentCount,
    StorageSize,
}
