namespace AskLucy.Application.Abstractions;

/// <summary>Whether a retrieval attempt produced grounded context (research.md Decision 8) — mirrors <see cref="AskLucy.Domain.Retrieval.RetrievalOutcome"/> plus the actual payload a caller needs.</summary>
public enum RagRetrievalOutcomeType
{
    Grounded,
    NoRelevantContent,
    Unavailable,
}

/// <summary>One citation-worthy chunk backing a grounded retrieval outcome (spec.md FR-030).</summary>
public sealed record RagCitationContext(
    Guid DocumentChunkId, Guid KnowledgeBaseId, Guid DocumentId, Guid DocumentVersionId,
    string DocumentTitle, string KnowledgeBaseName, int? PageNumber, string? Section, string Excerpt);

/// <summary>The result of a retrieval attempt for one chat message (research.md Decision 8).</summary>
public sealed record RagRetrievalOutcome(
    RagRetrievalOutcomeType Type, string? ContextText, IReadOnlyList<RagCitationContext> Citations,
    string? UnavailableReason);

/// <summary>
/// The RAG retrieval abstraction (docs/ARCHITECTURE.md &#167;13). Called from
/// <c>SendChatMessageCommandHandler</c> before building the message list, only when the
/// conversation has one or more attached knowledge bases. Never throws for a retrieval-time
/// failure — returns <see cref="RagRetrievalOutcomeType.Unavailable"/> instead, so the caller can
/// degrade gracefully (still generate a response, just ungrounded) rather than fail the whole chat
/// message (spec.md FR-037a, constitution &#167;2.VIII No Silent Failures).
/// </summary>
public interface IRagService
{
    Task<RagRetrievalOutcome> RetrieveContextAsync(
        Guid userChatId, string query, IReadOnlyList<Guid> knowledgeBaseIds, CancellationToken cancellationToken = default);
}
