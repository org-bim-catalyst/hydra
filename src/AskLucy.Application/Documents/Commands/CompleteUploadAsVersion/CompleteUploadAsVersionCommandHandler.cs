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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.VersionCreated, document.Id,
            $"A new version ({version.VersionMajor}.{version.VersionMinor}) of \"{document.FileName}\" was created.", cancellationToken);
        await processingPipeline.EnqueueAsync(document.Id, version.Id, cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}
