using Microsoft.Extensions.Caching.Memory;

namespace AskLucy.Application.KnowledgeBases;

/// <summary>
/// Thin wrapper around <see cref="IMemoryCache"/> centralizing the dashboard-summary cache
/// key convention (research.md Decision 7, FR-035) — every mutating handler that changes a
/// count the summary reports (create/delete/purge/upload/delete-document) calls
/// <see cref="Invalidate"/> rather than duplicating the key format itself. A per-instance
/// `IMemoryCache`, not a distributed cache: this codebase has no distributed cache configured
/// anywhere yet, and introducing one for a single summary card would be new cross-cutting
/// infrastructure disproportionate to the problem (research.md Decision 7).
/// </summary>
public sealed class KnowledgeBaseDashboardSummaryCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static string KeyFor(string ownerId) => $"knowledge-base-dashboard-summary:{ownerId}";

    public bool TryGet(string ownerId, out KnowledgeBaseDashboardSummaryDto summary) =>
        cache.TryGetValue(KeyFor(ownerId), out summary!);

    public void Set(string ownerId, KnowledgeBaseDashboardSummaryDto summary) =>
        cache.Set(KeyFor(ownerId), summary, Ttl);

    public void Invalidate(string ownerId) => cache.Remove(KeyFor(ownerId));
}
