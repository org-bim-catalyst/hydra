using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.ReplaceDocument;

/// <summary>
/// Finalizes a chunked upload session as a new <see cref="DocumentVersion"/> of an existing
/// document (FR-038, FR-039) — the prior version's file/extracted content is never touched.
/// Deliberately bypasses <see cref="DocumentUploadFinalizer"/>'s cross-document duplicate check
/// (research.md): a replace is an intentional new-content upload, not a candidate for "did you
/// mean to link this to an existing document" resolution.
/// </summary>
public sealed class ReplaceDocumentCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IResumableUploadStorage resumableStorage,
    IDocumentRepository documentRepository,
    IDocumentFileValidator fileValidator,
    IFileStorage fileStorage,
    IDocumentProcessingPipeline processingPipeline,
    IProcessingNotifier processingNotifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ReplaceDocumentCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(ReplaceDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);
        session.EnsureInProgress();

        if (session.TargetDocumentId != request.DocumentId)
        {
            throw new DomainRuleViolationException("This upload session was not started as a replacement for this document.");
        }

        var sessionKey = session.Id.ToString();
        var receivedBytes = await resumableStorage.GetSizeAsync(sessionKey, cancellationToken);
        if (receivedBytes != session.DeclaredSizeBytes)
        {
            throw new DomainRuleViolationException(
                $"Upload is incomplete — {receivedBytes} of {session.DeclaredSizeBytes} declared bytes received.");
        }

        await using var content = await resumableStorage.OpenReadAsync(sessionKey, cancellationToken);

        var validation = await fileValidator.ValidateAsync(content, session.FileName, cancellationToken);
        if (!validation.IsValid)
        {
            throw new DomainRuleViolationException(validation.FailureReason ?? "The file content is not a supported document type.");
        }

        content.Position = 0;
        var hash = await DocumentUploadFinalizer.ComputeSha256Async(content, cancellationToken);
        content.Position = 0;
        var storedFileName = await fileStorage.SaveAsync(content, session.FileName, cancellationToken);

        var currentVersion = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Current version not found.");
        var (newMajor, newMinor) = request.Increment == VersionIncrement.Major
            ? (currentVersion.VersionMajor + 1, 0)
            : (currentVersion.VersionMajor, currentVersion.VersionMinor + 1);

        var checksum = DocumentChecksum.Create(hash, userId);
        var version = DocumentVersion.Create(
            document.Id, newMajor, newMinor, storedFileName, session.FileName, session.DeclaredSizeBytes, checksum.Id, userId);

        documentRepository.AddChecksum(checksum);
        documentRepository.AddVersion(version);
        document.SetCurrentVersion(version.Id, session.DeclaredSizeBytes, validation.DetectedType!.Value, userId);
        document.SetProcessingStatus(DocumentProcessingStatus.Queued, userId);

        session.Complete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await resumableStorage.DeleteAsync(sessionKey, cancellationToken);

        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.VersionCreated, document.Id,
            $"A new version ({version.VersionMajor}.{version.VersionMinor}) of \"{document.FileName}\" was created.", cancellationToken);
        await processingPipeline.EnqueueAsync(document.Id, version.Id, cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}
