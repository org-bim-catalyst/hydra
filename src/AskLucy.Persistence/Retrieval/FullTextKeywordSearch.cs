using System.Globalization;
using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Retrieval;

/// <summary>
/// Keyword relevance search over <c>DocumentChunks.Content</c>'s full-text index (research.md
/// Decision 6), via <c>CONTAINSTABLE</c>. Located in <c>AskLucy.Persistence</c> alongside
/// <see cref="SqlServerVectorStore"/> for the same reason (direct <see cref="AskLucyDbContext"/>/
/// raw-SQL access, no reference from <c>AskLucy.Infrastructure</c> to <c>AskLucy.Persistence</c>).
/// Returns an empty result set (never throws) when Full-Text Search isn't installed on the target
/// SQL Server instance (e.g., LocalDB) — <c>HybridSearchQuery</c> degrades to semantic-only
/// ranking in that case rather than failing the whole search.
/// </summary>
public sealed class FullTextKeywordSearch(AskLucyDbContext dbContext) : IKeywordSearchService
{
    public async Task<IReadOnlyList<KeywordSearchCandidate>> SearchAsync(
        string query, IReadOnlyList<Guid> knowledgeBaseIds, int topK, CancellationToken cancellationToken = default)
    {
        if (knowledgeBaseIds.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var isFullTextInstalled = await dbContext.Database
            .SqlQueryRaw<int>("SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int) AS Value")
            .FirstOrDefaultAsync(cancellationToken);

        if (isFullTextInstalled != 1)
        {
            return [];
        }

        var knowledgeBaseIdList = string.Join(
            ",", knowledgeBaseIds.Select(id => $"'{id.ToString("D", CultureInfo.InvariantCulture)}'"));

        var sql =
            "SELECT TOP (" + topK.ToString(CultureInfo.InvariantCulture) + ") c.[Id] AS DocumentChunkId, ft.[RANK] AS Rank " +
            "FROM [DocumentChunks] c " +
            "INNER JOIN CONTAINSTABLE([DocumentChunks], [Content], {0}) AS ft ON ft.[KEY] = c.[Id] " +
            "WHERE c.[DeletedAtUtc] IS NULL " +
            "AND c.[KnowledgeBaseId] IN (" + knowledgeBaseIdList + ") " +
            "ORDER BY ft.[RANK] DESC";

        var results = await dbContext.Database
            .SqlQueryRaw<KeywordRankRow>(sql, ToContainsQuery(query))
            .ToListAsync(cancellationToken);

        return results.Select(r => new KeywordSearchCandidate(r.DocumentChunkId, r.Rank)).ToList();
    }

    /// <summary>CONTAINSTABLE's RANK column is a plain SQL <c>int</c> (0–1000) — kept as <see cref="int"/> here for exact EF raw-SQL row mapping, converted to <see cref="double"/> only in the public <see cref="KeywordSearchCandidate"/>.</summary>

    /// <summary>Builds a CONTAINSTABLE-safe OR-of-terms query from free text, escaping embedded double quotes so the caller's query text can never break out of the full-text predicate.</summary>
    private static string ToContainsQuery(string query)
    {
        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Replace("\"", "\"\""))
            .Select(t => $"\"{t}*\"");

        return string.Join(" OR ", terms);
    }

    private sealed class KeywordRankRow
    {
        public Guid DocumentChunkId { get; init; }

        public int Rank { get; init; }
    }
}
