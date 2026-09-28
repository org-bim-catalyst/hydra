using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUploadAsNew;

public sealed class CompleteUploadAsNewCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IDocumentFileValidator fileValidator,
    IDocumentProcessingPipeline processingPipeline,
    IProcessingNotifier processingNotifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CompleteUploadAsNewCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(CompleteUploadAsNewCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);
        if (session.Status != DocumentUploadSessionStatus.PendingDuplicateResolution)
        {
            throw new DomainRuleViolationException("This upload session has no pending duplicate to resolve.");
        }

        // Re-detect the file type from the already-validated, already-saved permanent file — the
        // original validation result from CompleteUpload wasn't retained on the session, and
        // re-running the (cheap, magic-byte-only) validator is simpler than adding a field for it.
        await using var savedContent = await fileStorage.OpenReadAsync(session.PendingStoredFileName!, cancellationToken);
        var validation = await fileValidator.ValidateAsync(savedContent, session.FileName, cancellationToken);

        var documentId = Guid.CreateVersion7();
        var checksum = DocumentChecksum.Create(session.PendingChecksumHash!, userId);
        var version = DocumentVersion.Create(
            documentId, 1, 0, session.PendingStoredFileName!, session.FileName, session.DeclaredSizeBytes, checksum.Id, userId);
        var document = Document.Create(documentId, userId, session.FileName, validation.DetectedType!.Value, session.DeclaredSizeBytes, version.Id, userId);

        documentRepository.AddChecksum(checksum);
        documentRepository.AddVersion(version);
        documentRepository.Add(document);

        session.Complete(userId);

        // T084 — before the save below, so the outbox event commits in the same unit of work.
        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.UploadCompleted, document.Id,
            dedupeKey: version.Id.ToString(), documentName: document.FileName, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await processingPipeline.EnqueueAsync(document.Id, version.Id, cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}
