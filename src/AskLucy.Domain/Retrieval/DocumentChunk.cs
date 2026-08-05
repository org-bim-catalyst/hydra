using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>The chunking strategy used to produce a chunk (spec.md FR-001).</summary>
public enum ChunkingStrategy
{
    FixedSize,
    Recursive,
    Paragraph,
    Sentence,
    Markdown,
    Heading,
    Table,
    Semantic,
}

/// <summary>
/// A segment of a document's extracted content produced by a chunking strategy (spec.md
/// FR-001–FR-005, data-model.md). Immutable after creation — a content change always produces a
/// new chunk rather than an in-place edit, so any <see cref="Embedding"/> history against it
/// remains unambiguous. <see cref="KnowledgeBaseId"/> is denormalized from
/// <see cref="KnowledgeBaseDocumentId"/>'s owning knowledge base at chunk-creation time, avoiding a
/// join on every search's authorization/scoping filter (FR-045, constitution §15).
/// </summary>
public sealed class DocumentChunk : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public Guid KnowledgeBaseDocumentId { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public ChunkingStrategy ChunkingStrategy { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public int TokenCount { get; private set; }

    public int CharacterCount { get; private set; }

    public string? Language { get; private set; }

    public int? PageNumber { get; private set; }

    public string? Section { get; private set; }

    public string? Heading { get; private set; }

    public int Position { get; private set; }

    private DocumentChunk()
    {
        // Required by EF Core materialization.
    }

    public static DocumentChunk Create(
        Guid knowledgeBaseId, Guid knowledgeBaseDocumentId, Guid documentId, Guid documentVersionId,
        ChunkingStrategy chunkingStrategy, string content, string contentHash, int tokenCount,
        int characterCount, string? language, int? pageNumber, string? section, string? heading,
        int position, string actor)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleViolationException("A chunk must have content.");
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new DomainRuleViolationException("A chunk must have a content hash.");
        }

        return new DocumentChunk
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            KnowledgeBaseDocumentId = knowledgeBaseDocumentId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            ChunkingStrategy = chunkingStrategy,
            Content = content,
            ContentHash = contentHash,
            TokenCount = tokenCount,
            CharacterCount = characterCount,
            Language = language,
            PageNumber = pageNumber,
            Section = section,
            Heading = heading,
            Position = position,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-016 — excludes this chunk from search when its source document is deleted, archived, or superseded by a version restore.</summary>
    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
