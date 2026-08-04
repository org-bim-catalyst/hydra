using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.RenameFolder;

public sealed class RenameFolderCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RenameFolderCommand, KnowledgeBaseFolderDto>
{
    public async Task<KnowledgeBaseFolderDto> Handle(RenameFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);
        var folder = KnowledgeBaseFolderGuard.EnsureBelongsTo(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), request.KnowledgeBaseId);

        folder.Rename(request.Name, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseFolderDto.FromEntity(folder);
    }
}
