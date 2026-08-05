using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CreateFolder;

public sealed class CreateFolderCommandHandler(
    IDocumentFolderRepository folderRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateFolderCommand, DocumentFolderDto>
{
    public async Task<DocumentFolderDto> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var depth = 0;
        if (request.ParentFolderId is { } parentId)
        {
            var parent = DocumentFolderOwnershipGuard.EnsureOwnedBy(
                await folderRepository.GetByIdAsync(parentId, cancellationToken), userId);
            depth = parent.Depth + 1;
        }

        var folder = DocumentFolder.Create(userId, request.Name, request.ParentFolderId, depth, userId);
        folderRepository.Add(folder);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DocumentFolderDto.FromEntity(folder, documentCount: 0);
    }
}
