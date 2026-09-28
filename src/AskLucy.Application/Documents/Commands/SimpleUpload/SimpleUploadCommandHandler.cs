using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.SimpleUpload;

public sealed class SimpleUploadCommandHandler(
    DocumentUploadFinalizer finalizer,
    IDocumentUploadSessionRepository sessionRepository,
    IDocumentProcessingPipeline processingPipeline,
    IProcessingNotifier processingNotifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SimpleUploadCommand, SimpleUploadResultDto>
{
    public async Task<SimpleUploadResultDto> Handle(SimpleUploadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var result = await finalizer.FinalizeAsync(userId, request.FileName, request.Content, request.SizeBytes, userId, cancellationToken);

        if (result.IsDuplicate)
        {
            var session = DocumentUploadSession.Create(userId, request.FileName, request.SizeBytes, request.SizeBytes, DateTime.UtcNow.AddHours(24), userId);
            session.MarkPendingDuplicateResolution(result.StoredFileName, result.ChecksumHash, userId);
            sessionRepository.Add(session);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new SimpleUploadResultDto(true, result.DuplicateOfDocumentId, session.Id, null);
        }

        // T084 — before the save below, so the outbox event commits in the same unit of work.
        await processingNotifier.NotifyAsync(
            userId, DocumentNotificationEventType.UploadCompleted, result.Document!.Id,
            dedupeKey: result.Document.CurrentVersionId.ToString(), documentName: result.Document.FileName, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await processingPipeline.EnqueueAsync(result.Document.Id, result.Document.CurrentVersionId, cancellationToken);

        return new SimpleUploadResultDto(false, null, null, DocumentSummaryDto.FromEntity(result.Document));
    }
}
