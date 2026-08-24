using AskLucy.Domain.Common;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A user's saved chat entry — the persisted "Conversation" business concept described in
/// specs/002-chat-history-management/spec.md (research.md Topic 1: this extends the entity
/// rather than introducing a parallel "Conversation" type). Migrated from the legacy
/// int-keyed <c>UserChats</c> table onto the standard entity conventions (spec.md FR-024
/// of SPEC-000); title/session/timestamp fields are otherwise unchanged from that migration.
/// Archive/Pin/Favorite/permanent-delete lifecycle state was added for SPEC-002.
/// </summary>
public sealed class UserChat : BaseEntity
{
    public string Title { get; private set; } = string.Empty;

    public string? SessionId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    /// <summary>Once true, automatic title generation (FR-013/FR-014) never overwrites <see cref="Title"/> again.</summary>
    public bool IsTitleManuallySet { get; private set; }

    /// <summary>Non-null = archived (FR-006/FR-007); independent of Pinned/Favorite/deleted state.</summary>
    public DateTime? ArchivedAtUtc { get; private set; }

    /// <summary>Non-null = pinned (FR-008); also the sort key for pinned-first ordering.</summary>
    public DateTime? PinnedAtUtc { get; private set; }

    /// <summary>Favorite flag (FR-009), independent of archive/pin/deleted state.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>The conversation's current provider (specs/005-multi-provider-ai-engine FR-008/FR-009). Unlike <see cref="Message.Provider"/>, this is a live FK — it reflects "what happens next," so it must be able to go stale when a provider is disabled (FR-018's fallback behavior detects that).</summary>
    public Guid? ProviderId { get; private set; }

    /// <summary>The conversation's current model, same reasoning as <see cref="ProviderId"/>.</summary>
    public Guid? ModelId { get; private set; }

    /// <summary>Conversation-level generation parameter overrides (FR-014), inherited by new messages unless overridden per-send.</summary>
    public string? GenerationParametersJson { get; private set; }

    /// <summary>Retrieval settings overrides (spec.md FR-020, FR-023, FR-024, research.md Decision 10) — null means "use the system default," same convention as <see cref="GenerationParametersJson"/>.</summary>
    public SearchMode? RetrievalSearchMode { get; private set; }

    public int? RetrievalTopK { get; private set; }

    public decimal? RetrievalSimilarityThreshold { get; private set; }

    public int? RetrievalMaxContextTokens { get; private set; }

    /// <summary>spec.md FR-002/FR-002a (specs/018-ai-memory-system, research.md Decision 1) — nullable = general (unscoped) memory. A conversation MAY belong to at most one Project at a time, mutated only via <see cref="AssignToProject"/>.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>
    /// The checkpoint <c>MemoryExtractionSweepJob</c> (specs/018-ai-memory-system, research.md
    /// Decision 6) reads to find conversations updated since their last analysis pass that the
    /// per-turn enqueue (<see cref="TouchLastActivity"/>-adjacent, invoked from
    /// <c>SendChatMessageCommandHandler</c>) hasn't already covered — e.g. a turn whose
    /// per-turn enqueue itself failed before Hangfire accepted the job. Null means "never
    /// analyzed."
    /// </summary>
    public DateTime? LastMemoryAnalyzedAtUtc { get; private set; }

    private UserChat()
    {
        // Required by EF Core materialization.
    }

    public static UserChat Create(string title, string userId, string? sessionId, string actor)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A chat title is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A chat must belong to a user.");
        }

        return new UserChat
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            SessionId = sessionId,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = userId,
        };
    }

    public void Rename(string newTitle, string actor)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new DomainRuleViolationException("A chat title is required.");
        }

        Title = newTitle.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Marks the title as manually set (FR-014) — called by the rename command handler, not by <see cref="Rename"/> itself, so callers that need a plain title change without freezing auto-titling (none today) remain possible.</summary>
    public void MarkTitleManuallySet() => IsTitleManuallySet = true;

    /// <summary>
    /// Applies an automatically-derived title (FR-013). No-ops once <see cref="IsTitleManuallySet"/>
    /// is true (FR-014) — encapsulated here so every caller gets the rule for free.
    /// </summary>
    public void ApplyAutoGeneratedTitle(string title, string actor)
    {
        if (IsTitleManuallySet || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        Title = title.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Bumps the last-activity timestamp without changing any other state — called whenever a message is appended, so the "recently updated" sort (FR-021) reflects message activity, not just conversation-level edits (rename/pin/archive/favorite).</summary>
    public void TouchLastActivity(string actor)
    {
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    /// <summary>Restores from Archived or Recently Deleted back to the default view (FR-005a/FR-007), preserving prior pin/favorite state — those flags were never touched by archive/delete, so there is nothing to restore on them.</summary>
    public void Restore(string actor)
    {
        DeletedAtUtc = null;
        DeletedBy = null;
        ArchivedAtUtc = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Archive(string actor)
    {
        if (ArchivedAtUtc is not null)
        {
            return;
        }

        ArchivedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Pin(string actor)
    {
        PinnedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Unpin(string actor)
    {
        if (PinnedAtUtc is null)
        {
            return;
        }

        PinnedAtUtc = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkFavorite(string actor)
    {
        if (IsFavorite)
        {
            return;
        }

        IsFavorite = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void UnmarkFavorite(string actor)
    {
        if (!IsFavorite)
        {
            return;
        }

        IsFavorite = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-008/FR-009/FR-014: applies to messages sent after this call only — prior messages keep the attribution already stamped onto them (FR-011).</summary>
    public void SetModelSelection(Guid providerId, Guid modelId, string? generationParametersJson, string actor)
    {
        ProviderId = providerId;
        ModelId = modelId;
        GenerationParametersJson = generationParametersJson;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public bool IsOwnedBy(string userId) => UserId == userId;

    /// <summary>FR-037 — applies to messages sent after this call only; prior messages' <c>RetrievalHistory</c>/citations are unaffected (each field may be null to revert to the system default).</summary>
    public void UpdateRetrievalSettings(SearchMode? searchMode, int? topK, decimal? similarityThreshold, int? maxContextTokens, string actor)
    {
        RetrievalSearchMode = searchMode;
        RetrievalTopK = topK;
        RetrievalSimilarityThreshold = similarityThreshold;
        RetrievalMaxContextTokens = maxContextTokens;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>spec.md FR-002a — pass null to remove this conversation from its Project (back to general scope).</summary>
    public void AssignToProject(Guid? projectId, string actor)
    {
        ProjectId = projectId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Records that memory extraction has just processed this conversation's turns up to now (specs/018-ai-memory-system, research.md Decision 6) — does not touch <see cref="ModifiedAtUtc"/>, since this is a system bookkeeping update, not a user-visible change.</summary>
    public void MarkMemoryAnalyzed() => LastMemoryAnalyzedAtUtc = DateTime.UtcNow;

    /// <summary>
    /// specs/037-location-query-resolution FR-004/FR-014 — persists the agent-confirmed
    /// location so back-references in a later turn can re-emit it without a new geocoding call.
    /// </summary>
    public void SetActiveLocation(double latitude, double longitude, string locationName, double confidence, string actor)
    {
        ActiveLocation = new ActiveSiteLocation(latitude, longitude, locationName, confidence);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>specs/037-location-query-resolution — the currently confirmed viewer location; null until the first successful location resolution for this chat.</summary>
    public ActiveSiteLocation? ActiveLocation { get; private set; }
}
