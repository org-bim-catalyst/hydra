using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-043 — preview generation. PDF/image formats render via a registered
/// <see cref="IDocumentPreviewGenerator"/>. Office formats (Word/Excel/PowerPoint) reuse the
/// already-extracted structure from the Text Extraction stage as a
/// <see cref="DocumentPreviewType.StructuredContent"/> preview (research.md Decision 6) — no
/// image rendering, no second library. Formats with no preview support at all (RTF/HTML/CSV/
/// JSON/XML/Text/Markdown) get no <see cref="DocumentPreview"/> row — <c>GetDocumentPreview</c>
/// (US7) reports "Unavailable" when none exists, never an error (FR-044).
/// </summary>
public sealed class PreviewGenerationStageHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IEnumerable<IDocumentPreviewGenerator> previewGenerators) : IProcessingStageHandler
{
    private static readonly HashSet<DocumentFileType> StructuredContentTypes =
        [DocumentFileType.Word, DocumentFileType.Excel, DocumentFileType.PowerPoint];

    public DocumentProcessingStageType StageType => DocumentProcessingStageType.PreviewGeneration;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");

        if (StructuredContentTypes.Contains(document.FileType))
        {
            documentRepository.AddPreview(
                DocumentPreview.Create(documentVersionId, DocumentPreviewType.StructuredContent, storedFileName: null, pageNumber: null, "system:processing"));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ProcessingStageOutcome.Completed;
        }

        var generator = previewGenerators.FirstOrDefault(g => g.CanHandle(document.FileType));
        if (generator is null)
        {
            return ProcessingStageOutcome.Skipped; // No preview support for this format.
        }

        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        var previewArtifacts = await generator.GenerateAsync(content, document.FileType, cancellationToken);

        foreach (var artifact in previewArtifacts.Where(a => a.Content is not null))
        {
            using var artifactStream = new MemoryStream(artifact.Content!);
            var storedFileName = await fileStorage.SaveAsync(artifactStream, $"{version.Id}-preview.png", cancellationToken);
            documentRepository.AddPreview(
                DocumentPreview.Create(documentVersionId, artifact.PreviewType, storedFileName, artifact.PageNumber, "system:processing"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ProcessingStageOutcome.Completed;
    }
}
