using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-021 — OCR for scanned PDFs and raster images. A PDF with an existing extractable text
/// layer skips OCR entirely (checked via the same <see cref="IDocumentTextExtractor"/> that
/// will run for real in the Text Extraction stage — a small amount of redundant work, traded
/// for not threading a skip-decision across two independently-resumable stages). PDF page
/// rasterization reuses the registered <see cref="IDocumentPreviewGenerator"/> for PDFs rather
/// than introducing a second PDF-to-image abstraction.
/// </summary>
public sealed class OcrStageHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IEnumerable<IDocumentTextExtractor> textExtractors,
    IEnumerable<IDocumentPreviewGenerator> previewGenerators,
    IOcrEngine ocrEngine) : IProcessingStageHandler
{
    public DocumentProcessingStageType StageType => DocumentProcessingStageType.Ocr;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");
        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        var isImage = document.FileType is DocumentFileType.Png or DocumentFileType.Jpeg or DocumentFileType.Tiff or DocumentFileType.Bmp or DocumentFileType.Webp;
        var isPdf = document.FileType == DocumentFileType.Pdf;

        if (!isImage && !isPdf)
        {
            return ProcessingStageOutcome.Skipped; // Nothing to OCR for non-image, non-PDF formats.
        }

        if (isPdf && await HasExistingTextLayerAsync(version, cancellationToken))
        {
            return ProcessingStageOutcome.Skipped; // Already has a text layer.
        }

        var ocrText = isImage
            ? await OcrImageAsync(version, cancellationToken)
            : await OcrPdfPagesAsync(version, cancellationToken);

        version.ApplyOcrText(ocrText, "system:processing");
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ProcessingStageOutcome.Completed;
    }

    private async Task<bool> HasExistingTextLayerAsync(DocumentVersion version, CancellationToken cancellationToken)
    {
        var extractor = textExtractors.First(e => e.CanHandle(DocumentFileType.Pdf));
        await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        var result = await extractor.ExtractAsync(content, DocumentFileType.Pdf, cancellationToken);
        return !string.IsNullOrWhiteSpace(result.PlainText);
    }

    private async Task<string?> OcrImageAsync(DocumentVersion version, CancellationToken cancellationToken)
    {
        await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        var result = await ocrEngine.RecognizeAsync(content, ["en"], cancellationToken);
        return result.RecognizedText;
    }

    private async Task<string?> OcrPdfPagesAsync(DocumentVersion version, CancellationToken cancellationToken)
    {
        var previewGenerator = previewGenerators.First(g => g.CanHandle(DocumentFileType.Pdf));
        await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        var pageImages = await previewGenerator.GenerateAsync(content, DocumentFileType.Pdf, cancellationToken);

        var builder = new StringBuilder();
        foreach (var pageImage in pageImages.Where(p => p.Content is not null))
        {
            using var imageStream = new MemoryStream(pageImage.Content!);
            var result = await ocrEngine.RecognizeAsync(imageStream, ["en"], cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.RecognizedText))
            {
                builder.AppendLine(result.RecognizedText);
            }
        }

        return builder.Length > 0 ? builder.ToString().Trim() : null;
    }
}
