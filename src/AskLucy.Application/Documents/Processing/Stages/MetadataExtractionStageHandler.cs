using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-023 — automatic metadata extraction (title, author, dates, keywords, encoding). Reuses
/// the same <see cref="IDocumentTextExtractor"/> as the Text Extraction stage (a second,
/// independent read — the extraction result itself isn't persisted between stages, mirroring
/// the OCR stage's equivalent trade-off) for OOXML/PDF core properties; plain-text formats get
/// only an encoding label.
/// </summary>
public sealed class MetadataExtractionStageHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IEnumerable<IDocumentTextExtractor> textExtractors) : IProcessingStageHandler
{
    public DocumentProcessingStageType StageType => DocumentProcessingStageType.MetadataExtraction;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");
        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        var extractor = textExtractors.FirstOrDefault(e => e.CanHandle(document.FileType));
        string? title = null, author = null, keywords = null, encoding = null;
        DateTime? creationDate = null, modificationDate = null;

        if (extractor is not null)
        {
            await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
            var result = await extractor.ExtractAsync(content, document.FileType, cancellationToken);
            title = result.Title;
            author = result.Author;
            creationDate = result.CreationDateUtc;
            modificationDate = result.ModificationDateUtc;
            keywords = result.Keywords;
        }
        else
        {
            encoding = "UTF-8";
        }

        // Reprocessing a replaced version (US5) must not violate DocumentMetadata's
        // one-row-per-document constraint — update the existing row (unless a user already
        // customized it) instead of blindly adding a second one.
        var existingMetadata = await documentRepository.GetMetadataByDocumentIdAsync(documentId, cancellationToken);
        if (existingMetadata is not null)
        {
            existingMetadata.ApplyReExtraction(title, author, creationDate, modificationDate, keywords, encoding, "system:processing");
        }
        else
        {
            documentRepository.AddMetadata(DocumentMetadata.CreateFromExtraction(
                documentId, title, author, creationDate, modificationDate, keywords, encoding, "system:processing"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ProcessingStageOutcome.Completed;
    }
}
