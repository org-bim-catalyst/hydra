using AskLucy.Application.Abstractions;
using AskLucy.Application.Retrieval.Queries.HybridSearch;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Retrieval;

/// <summary>
/// Implements <see cref="IRagService"/> (research.md Decision 8) — runs a <see cref="HybridSearchQuery"/>
/// (hybrid/system-default until US3's per-conversation search-mode override exists) using the
/// system-default top-K/similarity-threshold/max-context-tokens from
/// <c>contracts/conversation-retrieval-api.md</c>'s example values, then trims lower-ranked
/// results that would exceed the context-token budget (FR-024) before assembling
/// <see cref="RagRetrievalOutcome"/>.
///
/// <para>Never throws: any failure in the search pipeline (embedding-provider or vector-store
/// outage) is logged here — so the failure isn't silently discarded (constitution &#167;2.VIII) —
/// and converted into <see cref="RagRetrievalOutcomeType.Unavailable"/> rather than propagated,
/// per <see cref="IRagService"/>'s contract.</para>
/// </summary>
public sealed class RagService(IMediator mediator, ILogger<RagService> logger) : IRagService
{
    private const int DefaultTopK = 8;
    private const decimal DefaultSimilarityThreshold = 0.7m;
    private const int DefaultMaxContextTokens = 4000;

    public async Task<RagRetrievalOutcome> RetrieveContextAsync(
        Guid userChatId, string query, IReadOnlyList<Guid> knowledgeBaseIds, CancellationToken cancellationToken = default)
    {
        if (knowledgeBaseIds.Count == 0)
        {
            return new RagRetrievalOutcome(RagRetrievalOutcomeType.NoRelevantContent, null, [], null);
        }

        IReadOnlyList<SearchResultItemDto> results;
        try
        {
            results = await mediator.Send(
                new HybridSearchQuery(query, knowledgeBaseIds, null, DefaultTopK, DefaultSimilarityThreshold), cancellationToken);
        }
        catch (Exception ex)
        {
            RagServiceLog.RetrievalFailed(logger, userChatId, ex);
            return new RagRetrievalOutcome(RagRetrievalOutcomeType.Unavailable, null, [], "The knowledge base search service is temporarily unavailable.");
        }

        if (results.Count == 0)
        {
            return new RagRetrievalOutcome(RagRetrievalOutcomeType.NoRelevantContent, null, [], null);
        }

        var citations = new List<RagCitationContext>();
        var contextBuilder = new System.Text.StringBuilder();
        var usedTokens = 0;

        foreach (var result in results)
        {
            var excerptTokens = EstimateTokenCount(result.Excerpt);
            if (usedTokens + excerptTokens > DefaultMaxContextTokens && citations.Count > 0)
            {
                break; // FR-024 — trim lower-ranked chunks once the budget would be exceeded (results are already ranked best-first).
            }

            contextBuilder.AppendLine(result.Excerpt);
            usedTokens += excerptTokens;
            citations.Add(new RagCitationContext(
                result.ChunkId, result.KnowledgeBaseId, result.DocumentId, result.DocumentVersionId,
                result.DocumentTitle, result.KnowledgeBaseName, result.PageNumber, result.Section, result.Excerpt));
        }

        return new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, contextBuilder.ToString().TrimEnd(), citations, null);
    }

    /// <summary>Mirrors the identical heuristic in `Infrastructure/Retrieval/Chunking/ChunkTextHelpers.EstimateTokenCount` — duplicated rather than referenced, since `Application` cannot depend on `Infrastructure` (constitution §3).</summary>
    private static int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);
}

internal static partial class RagServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG retrieval failed for chat {UserChatId}")]
    public static partial void RetrievalFailed(ILogger logger, Guid userChatId, Exception exception);
}
