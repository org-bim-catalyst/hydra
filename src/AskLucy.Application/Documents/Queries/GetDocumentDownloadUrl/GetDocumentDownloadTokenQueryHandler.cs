using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentDownloadUrl;

public sealed class GetDocumentDownloadTokenQueryHandler(
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetDocumentDownloadTokenQuery, DocumentDownloadTokenDto>
{
    public async Task<DocumentDownloadTokenDto> Handle(GetDocumentDownloadTokenQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var versionId = request.VersionId ?? document.CurrentVersionId;
        var version = await documentRepository.GetVersionByIdAsync(versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Version not found.");

        if (version.DocumentId != document.Id)
        {
            throw new KeyNotFoundException("Version not found.");
        }

        return new DocumentDownloadTokenDto(version.Id, version.OriginalFileName);
    }
}
