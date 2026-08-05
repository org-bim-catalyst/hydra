using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.DuplicateDocument;

/// <summary>
/// FR-034 — an independent copy: its own stored file (a real byte-for-byte copy, not a shared
/// reference), its own metadata/classification/tags rows, and a fresh processing history (a new
/// <see cref="DocumentProcessingJob"/> via <see cref="IDocumentProcessingPipeline.EnqueueAsync"/> —
/// none of the source document's <see cref="DocumentProcessingLog"/> entries carry over).
/// </summary>
public sealed class DuplicateDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IDocumentProcessingPipeline processingPipeline,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicateDocumentCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(DuplicateDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var source = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);
        var sourceVersion = await documentRepository.GetVersionByIdAsync(source.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Current version not found.");

        await using var sourceContent = await fileStorage.OpenReadAsync(sourceVersion.StoredFileName, cancellationToken);
        var copiedStoredFileName = await fileStorage.SaveAsync(sourceContent, sourceVersion.OriginalFileName, cancellationToken);

        // Reuses the source's content hash rather than recomputing it — the byte copy just made
        // is guaranteed identical, and DocumentChecksum has no dedicated lookup of its own (it's
        // a 1:1 child row read only via its owning version elsewhere in this codebase).
        var checksumHash = await documentRepository.GetChecksumHashAsync(sourceVersion.ChecksumId, cancellationToken)
            ?? throw new KeyNotFoundException("Checksum not found.");
        var checksum = DocumentChecksum.Create(checksumHash, userId);

        var newDocumentId = Guid.CreateVersion7();
        var newVersion = DocumentVersion.Create(
            newDocumentId, 1, 0, copiedStoredFileName, sourceVersion.OriginalFileName, sourceVersion.SizeBytes, checksum.Id, userId);
        var newDocument = Document.Create(newDocumentId, userId, source.FileName, source.FileType, source.SizeBytes, newVersion.Id, userId);

        documentRepository.AddChecksum(checksum);
        documentRepository.AddVersion(newVersion);
        documentRepository.Add(newDocument);

        var sourceMetadata = await documentRepository.GetMetadataByDocumentIdAsync(source.Id, cancellationToken);
        if (sourceMetadata is not null)
        {
            documentRepository.AddMetadata(DocumentMetadata.CreateCopy(newDocumentId, sourceMetadata, userId));
        }

        var sourceClassification = await documentRepository.GetClassificationByDocumentIdAsync(source.Id, cancellationToken);
        if (sourceClassification is not null)
        {
            documentRepository.AddClassification(DocumentClassification.CreateCopy(newDocumentId, sourceClassification, userId));
        }

        foreach (var tag in source.Tags)
        {
            newDocument.AddTag(tag, userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await processingPipeline.EnqueueAsync(newDocumentId, newVersion.Id, cancellationToken);

        return DocumentSummaryDto.FromEntity(newDocument);
    }
}
