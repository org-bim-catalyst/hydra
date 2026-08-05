using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// A record of a direct (non-conversation) search a user performed (spec.md FR-043,
/// data-model.md). Append-only; distinct from <see cref="RetrievalHistory"/> (conversation-scoped
/// retrieval).
/// </summary>
public sealed class SearchHistory : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public string Query { get; private set; } = string.Empty;

    public SearchMode SearchMode { get; private set; }

    public string KnowledgeBaseIdsSearchedJson { get; private set; } = "[]";

    public string? FiltersJson { get; private set; }

    public int ResultCount { get; private set; }

    private SearchHistory()
    {
        // Required by EF Core materialization.
    }

    public static SearchHistory Create(string userId, string query, SearchMode searchMode, string knowledgeBaseIdsSearchedJson, string? filtersJson, int resultCount, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A search history entry must belong to a user.");
        }

        return new SearchHistory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Query = query,
            SearchMode = searchMode,
            KnowledgeBaseIdsSearchedJson = knowledgeBaseIdsSearchedJson,
            FiltersJson = filtersJson,
            ResultCount = resultCount,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
