using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Prompts.Commands.MoveFolder;

public sealed class MoveFolderCommandHandler(
    IPromptFolderRepository folderRepository,
    IOptions<PromptFolderOptions> folderOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<MoveFolderCommand, PromptFolderDto>
{
    public async Task<PromptFolderDto> Handle(MoveFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var folder = await folderRepository.GetByIdForOwnerAsync(request.FolderId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Folder not found.");

        var newParentDepth = 0;
        if (request.NewParentFolderId is { } newParentFolderId)
        {
            if (await folderRepository.IsSameOrDescendantAsync(newParentFolderId, request.FolderId, cancellationToken))
            {
                throw new DomainRuleViolationException("A folder cannot be moved into itself or one of its own subfolders.");
            }

            var newParent = await folderRepository.GetByIdForOwnerAsync(newParentFolderId, userId, cancellationToken)
                ?? throw new DomainRuleViolationException("The target parent folder does not exist.");
            newParentDepth = newParent.Depth;
        }

        folder.MoveTo(request.NewParentFolderId, newParentDepth, folderOptions.Value.MaxNestingDepth, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptFolderDto.FromEntity(folder);
    }
}
