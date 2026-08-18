using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RenameFolder;

public sealed class RenameFolderCommandHandler(
    IPromptFolderRepository folderRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RenameFolderCommand, PromptFolderDto>
{
    public async Task<PromptFolderDto> Handle(RenameFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var folder = await folderRepository.GetByIdForOwnerAsync(request.FolderId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Folder not found.");

        folder.Rename(request.Name, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptFolderDto.FromEntity(folder);
    }
}
