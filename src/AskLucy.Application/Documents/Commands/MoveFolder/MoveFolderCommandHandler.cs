using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Documents.Commands.MoveFolder;

/// <summary>FR-033, Edge Cases — a folder can never be moved into itself or one of its own descendants (that would create a cycle in the tree).</summary>
public sealed class MoveFolderCommandHandler(
    IDocumentFolderRepository folderRepository,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<MoveFolderCommand, DocumentFolderDto>
{
    public async Task<DocumentFolderDto> Handle(MoveFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var folder = DocumentFolderOwnershipGuard.EnsureOwnedBy(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), userId);

        var depth = 0;
        if (request.NewParentFolderId is { } newParentId)
        {
            var newParent = DocumentFolderOwnershipGuard.EnsureOwnedBy(
                await folderRepository.GetByIdAsync(newParentId, cancellationToken), userId);

            if (await folderRepository.IsSelfOrDescendantAsync(folder.Id, newParentId, cancellationToken))
            {
                throw new DomainRuleViolationException("A folder cannot be moved into itself or one of its own descendants.");
            }

            depth = newParent.Depth + 1;
        }

        folder.MoveTo(request.NewParentFolderId, depth, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var documentCount = await documentRepository.CountDocumentsInFolderAsync(folder.Id, cancellationToken);
        return DocumentFolderDto.FromEntity(folder, documentCount);
    }
}
