using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListKnowledgeBaseDocuments;

public sealed class ListKnowledgeBaseDocumentsQueryHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListKnowledgeBaseDocumentsQuery, PagedResult<KnowledgeBaseDocumentDto>>
{
    public async Task<PagedResult<KnowledgeBaseDocumentDto>> Handle(ListKnowledgeBaseDocumentsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);

        if (request.FolderId is { } folderId)
        {
            KnowledgeBaseFolderGuard.EnsureBelongsTo(await folderRepository.GetByIdAsync(folderId, cancellationToken), request.KnowledgeBaseId);
        }

        var documents = await documentRepository.ListByFolderAsync(request.KnowledgeBaseId, request.FolderId, cancellationToken);

        return new PagedResult<KnowledgeBaseDocumentDto>([.. documents.Select(KnowledgeBaseDocumentDto.FromEntity)], NextCursor: null);
    }
}
