namespace AskLucy.Application.Abstractions;

public enum IndexingOutcome
{
    Completed,
    PartiallyCompleted,
    Failed,
}

/// <summary>
/// Indexes one knowledge-base document end to end: chunk, embed, write to the vector store
/// (research.md Decision 2). Creates the underlying <c>Document</c>/<c>DocumentVersion</c> via the
/// Document Intelligence Pipeline's existing extraction/OCR when the knowledge-base document has no
/// <c>DocumentId</c> link yet — never re-implements parsing here (constitution &#167;18).
/// </summary>
public interface IIndexingOrchestrator
{
    Task<IndexingOutcome> IndexKnowledgeBaseDocumentAsync(
        Guid knowledgeBaseDocumentId, bool forceFullReindex, CancellationToken cancellationToken = default);
}
