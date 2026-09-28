using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;

public sealed class CompleteUploadAsVersionCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IDocumentRepository documentRepository,
    IDocumentProcessingPipeline processingPipeline,
    IProcessingNotifier processingNotifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CompleteUploadAsVersionCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(CompleteUploadAsVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);
        if (session.Status != DocumentUploadSessionStatus.PendingDuplicateResolution)
        {
            throw new DomainRuleViolationException("This upload session has no pending duplicate to resolve.");
        }

        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.ExistingDocumentId, cancellationToken), userId);
        var currentVersion = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Current version not found.");

        var (newMajor, newMinor) = request.Increment == VersionIncrement.Major
            ? (currentVersion.VersionMajor + 1, 0)
            : (currentVersion.VersionMajor, currentVersion.VersionMinor + 1);

        var checksum = DocumentChecksum.Create(session.PendingChecksumHash!, userId);
        var version = DocumentVersion.Create(
            document.Id, newMajor, newMinor, session.PendingStoredFileName!, session.FileName, session.DeclaredSizeBytes, checksum.Id, userId);

        documentRepository.AddChecksum(checksum);
        documentRepository.AddVersion(version);
        document.SetCurrentVersion(version.Id, session.DeclaredSizeBytes, document.FileType, userId);
        document.SetProcessingStatus(DocumentProcessingStatus.Queued, userId);

        session.Complete(userId);

        // T084 — before the save below, so the outbox event commits in the same unit of work.
        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.VersionCreated, document.Id,
            dedupeKey: version.Id.ToString(), documentName: document.FileName,
            versionNumber: $"{version.VersionMajor}.{version.VersionMinor}", cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await processingPipeline.EnqueueAsync(document.Id, version.Id, cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}
