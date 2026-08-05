using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>Which relevance mechanism a search or retrieval used (spec.md FR-017–FR-020).</summary>
public enum SearchMode
{
    Semantic,
    Keyword,
    Hybrid,
}

/// <summary>Whether a retrieval produced grounded context, found nothing relevant, or could not run at all (research.md Decision 8).</summary>
public enum RetrievalOutcome
{
    Grounded,
    NoRelevantContent,
    Unavailable,
}

/// <summary>
/// A record of a retrieval performed on behalf of a conversation message (spec.md FR-030–FR-037a,
/// data-model.md). Append-only.
/// </summary>
public sealed class RetrievalHistory : BaseEntity
{
    public Guid UserChatId { get; private set; }

    public Guid? MessageId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string Query { get; private set; } = string.Empty;

    public SearchMode SearchMode { get; private set; }

    public string KnowledgeBaseIdsSearchedJson { get; private set; } = "[]";

    public int TopK { get; private set; }

    public decimal SimilarityThreshold { get; private set; }

    public int MaxContextTokens { get; private set; }

    public RetrievalOutcome Outcome { get; private set; }

    public int DurationMs { get; private set; }

    public int ResultCount { get; private set; }

    private RetrievalHistory()
    {
        // Required by EF Core materialization.
    }

    public static RetrievalHistory Create(
        Guid userChatId, Guid? messageId, string userId, string query, SearchMode searchMode,
        string knowledgeBaseIdsSearchedJson, int topK, decimal similarityThreshold, int maxContextTokens,
        RetrievalOutcome outcome, int durationMs, int resultCount, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A retrieval history entry must belong to a user.");
        }

        return new RetrievalHistory
        {
            Id = Guid.CreateVersion7(),
            UserChatId = userChatId,
            MessageId = messageId,
            UserId = userId,
            Query = query,
            SearchMode = searchMode,
            KnowledgeBaseIdsSearchedJson = knowledgeBaseIdsSearchedJson,
            TopK = topK,
            SimilarityThreshold = similarityThreshold,
            MaxContextTokens = maxContextTokens,
            Outcome = outcome,
            DurationMs = durationMs,
            ResultCount = resultCount,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Backfilled once the assistant Message this retrieval served has been created (the message doesn't exist yet at retrieval time).</summary>
    public void AssignMessage(Guid messageId, string actor)
    {
        MessageId = messageId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
