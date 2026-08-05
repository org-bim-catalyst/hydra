using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUpload;

/// <summary>
/// Finalizes a chunked upload session (FR-005, FR-009, FR-020). Verifies every declared byte
/// arrived, validates content, computes the checksum, and either creates the new
/// <see cref="Domain.Documents.Document"/> (enqueuing processing) or reports a checksum
/// duplicate for the caller to resolve.
/// </summary>
public sealed class CompleteUploadCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IResumableUploadStorage resumableStorage,
    DocumentUploadFinalizer finalizer,
    IDocumentProcessingPipeline processingPipeline,
    IProcessingNotifier processingNotifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CompleteUploadCommand, CompleteUploadResultDto>
{
    public async Task<CompleteUploadResultDto> Handle(CompleteUploadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);
        session.EnsureInProgress();

        var sessionKey = session.Id.ToString();
        var receivedBytes = await resumableStorage.GetSizeAsync(sessionKey, cancellationToken);
        if (receivedBytes != session.DeclaredSizeBytes)
        {
            throw new DomainRuleViolationException(
                $"Upload is incomplete — {receivedBytes} of {session.DeclaredSizeBytes} declared bytes received.");
        }

        await using var content = await resumableStorage.OpenReadAsync(sessionKey, cancellationToken);
        var result = await finalizer.FinalizeAsync(userId, session.FileName, content, session.DeclaredSizeBytes, userId, cancellationToken);

        if (result.IsDuplicate)
        {
            session.MarkPendingDuplicateResolution(result.StoredFileName, result.ChecksumHash, userId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await resumableStorage.DeleteAsync(sessionKey, cancellationToken);

            return new CompleteUploadResultDto(true, result.DuplicateOfDocumentId, null);
        }

        session.Complete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await resumableStorage.DeleteAsync(sessionKey, cancellationToken);

        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.UploadCompleted, result.Document!.Id, $"\"{result.Document.FileName}\" uploaded successfully.", cancellationToken);
        await processingPipeline.EnqueueAsync(result.Document.Id, result.Document.CurrentVersionId, cancellationToken);

        return new CompleteUploadResultDto(false, null, DocumentSummaryDto.FromEntity(result.Document));
    }
}
