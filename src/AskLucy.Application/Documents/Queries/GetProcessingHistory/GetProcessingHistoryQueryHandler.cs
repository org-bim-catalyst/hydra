using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetProcessingHistory;

public sealed class GetProcessingHistoryQueryHandler(
    IDocumentRepository documentRepository,
    IDocumentProcessingJobRepository jobRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetProcessingHistoryQuery, IReadOnlyList<DocumentProcessingLogDto>>
{
    public async Task<IReadOnlyList<DocumentProcessingLogDto>> Handle(GetProcessingHistoryQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var logs = await jobRepository.GetLogsByDocumentIdAsync(document.Id, cancellationToken);
        return logs.Select(DocumentProcessingLogDto.FromEntity).ToList();
    }
}
