using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.Extensions.Logging;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory;

/// <summary>
/// Implements <see cref="IMemoryService"/> (research.md Decisions 3/4) — embeds the query,
/// narrows candidates via <see cref="IMemoryVectorStore"/>, then ranks by the composite score
/// <c>similarity × recencyDecay × importance × confidence</c> before token-budgeting the
/// selection, mirroring <c>RagService</c>'s shape (specs/016) exactly, including its
/// never-throws contract.
/// </summary>
public sealed class MemoryService(
    IMemoryPreferenceRepository preferenceRepository,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IMemoryVectorStore vectorStore,
    IMemoryRepository memoryRepository,
    ILogger<MemoryService> logger) : IMemoryService
{
    private const int DefaultTopK = 8;
    private const double DefaultSimilarityThreshold = 0.5;
    private const int DefaultMaxContextTokens = 1500;

    /// <summary>Recency decays to half-weight after this many days since a memory was last reinforced (research.md Decision 4).</summary>
    private const double RecencyHalfLifeDays = 30.0;

    public async Task<MemoryRetrievalOutcome> RetrieveRelevantMemoriesAsync(
        string userId, Guid userChatId, Guid? projectId, string query, CancellationToken cancellationToken = default)
    {
        try
        {
            var preference = await preferenceRepository.GetByUserIdAsync(userId, cancellationToken);
            if (preference is not null && !preference.MemoryEnabled)
            {
                return new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null);
            }

            var provider = await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken)
                ?? throw new InvalidOperationException("No default embedding provider is configured.");
            var embeddingService = embeddingServiceResolver.Resolve(provider.Vendor);
            var queryEmbedding = await embeddingService.EmbedAsync(query, cancellationToken);

            var candidates = await vectorStore.QueryNearestAsync(
                queryEmbedding.Vector, userId, projectId, DefaultTopK, DefaultSimilarityThreshold, cancellationToken);

            if (candidates.Count == 0)
            {
                return new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null);
            }

            var memories = await memoryRepository.GetActiveByIdsAsync(
                candidates.Select(c => c.MemoryId).ToList(), cancellationToken);
            var memoriesById = memories.ToDictionary(m => m.Id);

            // spec.md FR-025, User Story 4 AC4 — a category disabled at the usage level (distinct
            // from its ApprovalMode, see MemoryCategoryPreference's doc comment) must never be
            // used in retrieval, even though its existing memories remain stored, untouched.
            var categoryPreferences = await preferenceRepository.GetCategoryPreferencesAsync(userId, cancellationToken);
            var disabledCategories = categoryPreferences.Where(p => !p.IsEnabled).Select(p => p.Category).ToHashSet();

            var now = DateTime.UtcNow;
            var ranked = candidates
                .Where(c => memoriesById.ContainsKey(c.MemoryId) && !disabledCategories.Contains(memoriesById[c.MemoryId].Category))
                .Select(c => RankCandidate(memoriesById[c.MemoryId], c.Distance, now))
                .OrderByDescending(r => r.Score)
                .ToList();

            var selected = SelectWithinBudget(ranked);

            if (selected.Count == 0)
            {
                return new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null);
            }

            var contextText = string.Join(Environment.NewLine, selected.Select(s => "- " + s.Content));
            return new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Found, contextText, selected, null);
        }
        catch (Exception ex)
        {
            MemoryServiceLog.RetrievalFailed(logger, userChatId, ex);
            return new MemoryRetrievalOutcome(
                MemoryRetrievalOutcomeType.Unavailable, null, [], "The memory service is temporarily unavailable.");
        }
    }

    private static RankedMemory RankCandidate(MemoryEntity memory, double distance, DateTime nowUtc)
    {
        var similarity = (decimal)Math.Clamp(1.0 - distance, 0.0, 1.0);
        var ageDays = Math.Max(0.0, (nowUtc - memory.LastReinforcedAtUtc).TotalDays);
        var recencyDecay = (decimal)Math.Pow(0.5, ageDays / RecencyHalfLifeDays);
        var score = similarity * recencyDecay * memory.Importance * memory.Confidence;

        return new RankedMemory(memory, similarity, score);
    }

    private static List<MemoryReferenceContext> SelectWithinBudget(IReadOnlyList<RankedMemory> ranked)
    {
        var selected = new List<MemoryReferenceContext>();
        var usedTokens = 0;

        foreach (var candidate in ranked)
        {
            var tokens = EstimateTokenCount(candidate.Memory.Content);
            if (usedTokens + tokens > DefaultMaxContextTokens && selected.Count > 0)
            {
                break; // Best-ranked-first trimming once the token budget would be exceeded, mirroring RagService.
            }

            selected.Add(new MemoryReferenceContext(candidate.Memory.Id, candidate.Memory.Content, candidate.Similarity));
            usedTokens += tokens;
        }

        return selected;
    }

    /// <summary>Mirrors the identical heuristic in `RagService`/`ChunkTextHelpers.EstimateTokenCount` — duplicated rather than referenced, since `Application` cannot depend on `Infrastructure` (constitution §3).</summary>
    private static int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);

    private readonly record struct RankedMemory(MemoryEntity Memory, decimal Similarity, decimal Score);
}

internal static partial class MemoryServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory retrieval failed for chat {UserChatId}")]
    public static partial void RetrievalFailed(ILogger logger, Guid userChatId, Exception exception);
}
