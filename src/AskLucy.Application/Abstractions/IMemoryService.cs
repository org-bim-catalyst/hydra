namespace AskLucy.Application.Abstractions;

/// <summary>Whether a memory-retrieval attempt produced usable context (research.md Decision 3) — mirrors <see cref="RagRetrievalOutcomeType"/> plus the actual payload a caller needs.</summary>
public enum MemoryRetrievalOutcomeType
{
    Found,
    NoneRelevant,
    Unavailable,
}

/// <summary>One memory selected for a turn, carrying enough context to build a <c>MemoryReference</c> row (spec.md FR-014).</summary>
public sealed record MemoryReferenceContext(Guid MemoryId, string Content, decimal RelevanceScore);

/// <summary>The result of a memory-selection attempt for one chat message (research.md Decisions 3/4).</summary>
public sealed record MemoryRetrievalOutcome(
    MemoryRetrievalOutcomeType Type, string? ContextText, IReadOnlyList<MemoryReferenceContext> UsedMemories,
    string? UnavailableReason);

/// <summary>
/// The memory-selection/ranking abstraction (spec.md FR-010–FR-012, research.md Decisions 3–4).
/// Called from <c>SendChatMessageCommandHandler</c> before building the message list, mirroring
/// <see cref="IRagService"/>'s call-site shape exactly. Never throws for a retrieval-time failure
/// — returns <see cref="MemoryRetrievalOutcomeType.Unavailable"/> instead, so the caller can
/// degrade gracefully (still generate a response, just without memory context) rather than fail or
/// delay the chat message (spec.md FR-014a, clarified 2026-08-09; constitution §2.VIII).
/// </summary>
public interface IMemoryService
{
    Task<MemoryRetrievalOutcome> RetrieveRelevantMemoriesAsync(
        string userId, Guid userChatId, Guid? projectId, string query, CancellationToken cancellationToken = default);
}
