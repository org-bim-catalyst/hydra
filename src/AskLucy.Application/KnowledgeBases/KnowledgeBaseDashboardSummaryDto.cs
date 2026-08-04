namespace AskLucy.Application.KnowledgeBases;

/// <summary>Dashboard summary statistics cards (FR-029). `RecentCount` is knowledge bases updated within the last 7 days — a "how many showed up recently" count distinct from the Recent *section* itself (FR-027), which is just `view=Active&amp;sort=RecentlyUpdated` on the search endpoint, not a separate resource.</summary>
public sealed record KnowledgeBaseDashboardSummaryDto(
    int TotalKnowledgeBases,
    int TotalDocuments,
    long TotalStorageBytes,
    int RecentCount,
    int FavoritesCount,
    int PinnedCount,
    int ArchivedCount);
