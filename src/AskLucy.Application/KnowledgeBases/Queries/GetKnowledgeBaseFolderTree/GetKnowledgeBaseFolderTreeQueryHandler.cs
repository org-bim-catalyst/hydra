using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseFolderTree;

public sealed class GetKnowledgeBaseFolderTreeQueryHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetKnowledgeBaseFolderTreeQuery, KnowledgeBaseFolderTreeDto>
{
    public async Task<KnowledgeBaseFolderTreeDto> Handle(GetKnowledgeBaseFolderTreeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);

        var folders = await folderRepository.ListByKnowledgeBaseIdAsync(request.KnowledgeBaseId, cancellationToken);
        var rootDocuments = await documentRepository.ListByFolderAsync(request.KnowledgeBaseId, folderId: null, cancellationToken);

        return new KnowledgeBaseFolderTreeDto(
            [.. folders.Select(KnowledgeBaseFolderDto.FromEntity)],
            [.. rootDocuments.Select(KnowledgeBaseDocumentDto.FromEntity)]);
    }
}
