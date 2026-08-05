using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A source reference associated with an assistant <see cref="Message"/> (FR-017). Child of
/// <see cref="Message"/>'s aggregate, not independently reachable (constitution &#167;5) —
/// created only via <see cref="Message.AddCitation"/>/<see cref="Message.AddCitationFromChunk"/>.
/// The RAG-specific fields (research.md Decision 9) were added ahead of this in specs/002
/// specifically for the RAG engine (spec.md FR-030) — reused here rather than duplicated into a
/// new entity (constitution &#167;18). <see cref="SourceLabel"/>/<see cref="SourceReference"/>
/// remain the generic, always-populated display fields; for a RAG-sourced citation they are
/// captured once at creation time from the chunk's document title/section, so the citation's
/// basic display text never depends on a live join and survives the source becoming inaccessible
/// (FR-034) — only <see cref="DocumentChunkId"/>'s live resolvability determines
/// <c>sourceAvailable</c> at render time.
/// </summary>
public sealed class Citation : BaseEntity
{
    public Guid MessageId { get; private set; }

    public string SourceLabel { get; private set; } = string.Empty;

    public string? SourceReference { get; private set; }

    /// <summary>Soft-reference FK — populated for RAG-sourced citations only (FR-030).</summary>
    public Guid? DocumentChunkId { get; private set; }

    public Guid? KnowledgeBaseId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? DocumentVersionId { get; private set; }

    public int? PageNumber { get; private set; }

    public string? Section { get; private set; }

    private Citation()
    {
        // Required by EF Core materialization.
    }

    internal static Citation Create(Guid messageId, string sourceLabel, string? sourceReference, string actor)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            throw new DomainRuleViolationException("A citation source label is required.");
        }

        return new Citation
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            SourceLabel = sourceLabel,
            SourceReference = sourceReference,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>research.md Decision 9 — a citation grounded in a retrieved <c>DocumentChunk</c> (spec.md FR-030).</summary>
    internal static Citation CreateFromChunk(
        Guid messageId, string sourceLabel, string? sourceReference, Guid documentChunkId,
        Guid knowledgeBaseId, Guid documentId, Guid documentVersionId, int? pageNumber,
        string? section, string actor)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            throw new DomainRuleViolationException("A citation source label is required.");
        }

        return new Citation
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            SourceLabel = sourceLabel,
            SourceReference = sourceReference,
            DocumentChunkId = documentChunkId,
            KnowledgeBaseId = knowledgeBaseId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            PageNumber = pageNumber,
            Section = section,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
