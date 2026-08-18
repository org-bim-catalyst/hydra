using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeleteFolder;

public sealed class DeleteFolderCommandHandler(
    IPromptFolderRepository folderRepository,
    IPromptRepository promptRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteFolderCommand>
{
    public async Task Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var folder = await folderRepository.GetByIdForOwnerAsync(request.FolderId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Folder not found.");

        var prompts = await promptRepository.ListByFolderIdAsync(folder.Id, cancellationToken);
        foreach (var prompt in prompts)
        {
            prompt.SetFolder(null, userId);
        }

        folder.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
