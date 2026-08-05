using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-022 — structured text extraction. OOXML/PDF formats use a registered
/// <see cref="IDocumentTextExtractor"/>; plain-text-ish formats (Markdown/HTML/CSV/JSON/XML/
/// Text/RTF) are their own extracted text verbatim — no library needed. Image formats have
/// nothing to extract here (their content comes from the OCR stage's <c>OcrTextRaw</c> instead)
/// and this stage is Skipped for them.
/// </summary>
public sealed class TextExtractionStageHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IEnumerable<IDocumentTextExtractor> textExtractors) : IProcessingStageHandler
{
    private static readonly HashSet<DocumentFileType> PlainTextTypes =
    [
        DocumentFileType.Markdown, DocumentFileType.Html, DocumentFileType.Csv,
        DocumentFileType.Json, DocumentFileType.Xml, DocumentFileType.Text, DocumentFileType.Rtf,
    ];

    public DocumentProcessingStageType StageType => DocumentProcessingStageType.TextExtraction;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");
        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        var extractor = textExtractors.FirstOrDefault(e => e.CanHandle(document.FileType));

        if (extractor is not null)
        {
            await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
            var result = await extractor.ExtractAsync(content, document.FileType, cancellationToken);
            version.ApplyExtractedText(result.PlainText, result.StructureJson, result.PageCount, "system:processing");
        }
        else if (PlainTextTypes.Contains(document.FileType))
        {
            await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
            using var reader = new StreamReader(content, Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken);
            version.ApplyExtractedText(text, extractedStructureJson: null, pageCount: null, "system:processing");
        }
        else
        {
            return ProcessingStageOutcome.Skipped; // Image formats — nothing to extract here.
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProcessingStageOutcome.Completed;
    }
}
