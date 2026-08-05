using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetFolderTree;

public sealed class GetFolderTreeQueryHandler(
    IDocumentFolderRepository folderRepository,
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetFolderTreeQuery, IReadOnlyList<DocumentFolderDto>>
{
    public async Task<IReadOnlyList<DocumentFolderDto>> Handle(GetFolderTreeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var folders = await folderRepository.ListByOwnerAsync(userId, cancellationToken);
        var counts = await documentRepository.CountDocumentsByFolderAsync(userId, cancellationToken);

        return folders.Select(f => DocumentFolderDto.FromEntity(f, counts.GetValueOrDefault(f.Id))).ToList();
    }
}
