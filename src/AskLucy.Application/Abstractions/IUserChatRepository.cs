using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Aggregate-oriented repository for <see cref="UserChat"/> (constitution &#167;3 Repository rules).
/// </summary>
public interface IUserChatRepository
{
    Task<UserChat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks up a conversation by id bypassing the global soft-delete query filter (research.md Topic 2) — needed by Restore/Purge, which must act on an already-soft-deleted (Recently Deleted) conversation.</summary>
    Task<UserChat?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently, irreversibly removes a conversation and (via FK cascade) its messages/
    /// attachments/citations (FR-004) — the constitution's GDPR-style audited hard-delete
    /// path. Implemented as a bulk <c>ExecuteDelete</c> rather than load+Remove+SaveChanges:
    /// the Persistence layer's audit SaveChanges interceptor unconditionally converts every
    /// tracked hard delete on a <c>BaseEntity</c> into a soft delete, so a tracked Remove
    /// would silently fail to actually purge anything.
    /// </summary>
    Task PurgeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserChat>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(UserChat chat);

    /// <summary>specs/018-ai-memory-system, research.md Decision 6 — conversations touched since their own <see cref="UserChat.LastMemoryAnalyzedAtUtc"/> checkpoint (or never analyzed), for <c>MemoryExtractionSweepJob</c> to pick up turns the per-turn enqueue missed.</summary>
    Task<IReadOnlyList<UserChat>> ListNeedingMemoryAnalysisAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps "memory analysis ran" against a conversation, without going through the tracked
    /// entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UserChat"/> carries a rowversion, and the memory-extraction job is enqueued at
    /// the very end of a chat turn — the same moment the turn is still writing that row (the
    /// assistant messages, and for a location query the resolved boundary). The job would load
    /// the chat, those writes would bump the rowversion, and its own save then matched zero rows
    /// and threw <c>DbUpdateConcurrencyException</c>. Observed three times on 2026-09-01, each
    /// one immediately after a turn completed.
    /// </para>
    /// <para>
    /// A targeted update rather than optimistic concurrency because this timestamp is monotonic
    /// bookkeeping: last writer wins is the correct answer, and there is nothing here for a
    /// concurrent edit to corrupt. The rowversion still guards everything about the conversation
    /// that a user can actually change.
    /// </para>
    /// </remarks>
    Task MarkMemoryAnalyzedAsync(Guid chatId, DateTime analyzedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Cursor-paginated search/filter/sort (FR-019–FR-022, research.md Topics 5/6).</summary>
    Task<(IReadOnlyList<UserChat> Items, string? NextCursor)> SearchAsync(
        string userId,
        ConversationView view,
        bool? pinnedOnly,
        bool? favoriteOnly,
        string? query,
        ConversationSort sort,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}
