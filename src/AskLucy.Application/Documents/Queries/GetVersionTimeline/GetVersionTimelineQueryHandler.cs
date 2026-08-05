using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetVersionTimeline;

public sealed class GetVersionTimelineQueryHandler(
    IDocumentRepository documentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetVersionTimelineQuery, IReadOnlyList<DocumentVersionSummaryDto>>
{
    public async Task<IReadOnlyList<DocumentVersionSummaryDto>> Handle(GetVersionTimelineQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var versions = await documentRepository.GetVersionsByDocumentIdAsync(document.Id, cancellationToken);
        return versions.Select(v => DocumentVersionSummaryDto.FromEntity(v, document.CurrentVersionId)).ToList();
    }
}
