using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentProcessingStatus;

public sealed class GetDocumentProcessingStatusQueryHandler(
    IDocumentRepository documentRepository,
    IDocumentProcessingJobRepository jobRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetDocumentProcessingStatusQuery, DocumentProcessingStatusDto>
{
    public async Task<DocumentProcessingStatusDto> Handle(GetDocumentProcessingStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var job = await jobRepository.GetCurrentForDocumentAsync(document.Id, cancellationToken)
            ?? throw new KeyNotFoundException("No processing job exists for this document.");
        var stages = await jobRepository.GetStagesAsync(job.Id, cancellationToken);

        return DocumentProcessingStatusDto.FromEntities(document, job, stages);
    }
}
