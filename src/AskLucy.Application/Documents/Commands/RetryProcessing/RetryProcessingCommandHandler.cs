using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Documents.Processing;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RetryProcessing;

/// <summary>
/// FR-029 — re-enqueues a Failed processing job. <see cref="Domain.Documents.DocumentProcessingJob.Retry"/>
/// (invoked inside <see cref="IDocumentProcessingPipeline.RetryAsync"/>) throws
/// <see cref="Domain.Documents.ProcessingNotInFailedStateException"/> (409 Conflict) when the
/// current job isn't <c>Failed</c> — this handler does not duplicate that check.
/// </summary>
public sealed class RetryProcessingCommandHandler(
    IDocumentRepository documentRepository,
    IDocumentProcessingPipeline processingPipeline,
    ICurrentUserAccessor currentUser) : IRequestHandler<RetryProcessingCommand>
{
    public async Task Handle(RetryProcessingCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        await processingPipeline.RetryAsync(request.DocumentId, cancellationToken);
    }
}
