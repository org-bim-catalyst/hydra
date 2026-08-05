using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RenameFolder;

public sealed class RenameFolderCommandHandler(
    IDocumentFolderRepository folderRepository,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RenameFolderCommand, DocumentFolderDto>
{
    public async Task<DocumentFolderDto> Handle(RenameFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var folder = DocumentFolderOwnershipGuard.EnsureOwnedBy(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), userId);

        folder.Rename(request.Name, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var documentCount = await documentRepository.CountDocumentsInFolderAsync(folder.Id, cancellationToken);
        return DocumentFolderDto.FromEntity(folder, documentCount);
    }
}
