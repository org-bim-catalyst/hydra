namespace AskLucy.Application.Abstractions;

/// <summary>One keyword-relevance candidate (research.md Decision 6).</summary>
public sealed record KeywordSearchCandidate(Guid DocumentChunkId, double Rank);

/// <summary>
/// Keyword relevance search over chunk content (spec.md FR-018, research.md Decision 6).
/// Implemented in <c>AskLucy.Persistence</c> (<c>FullTextKeywordSearch</c>, over SQL Server Full-
/// Text Search) since it needs the same raw-SQL/<c>DbContext</c> access <c>IVectorStore</c>'s
/// implementation does.
/// </summary>
public interface IKeywordSearchService
{
    Task<IReadOnlyList<KeywordSearchCandidate>> SearchAsync(
        string query, IReadOnlyList<Guid> knowledgeBaseIds, int topK, CancellationToken cancellationToken = default);
}
