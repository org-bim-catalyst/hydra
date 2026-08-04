using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.KnowledgeBases.Commands.MoveFolder;

/// <summary>Rejects moving a folder into itself or one of its own descendants (FR-013) via <see cref="IKnowledgeBaseFolderRepository.IsSameOrDescendantAsync"/>.</summary>
public sealed class MoveFolderCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IOptions<KnowledgeBaseFolderOptions> folderOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<MoveFolderCommand, KnowledgeBaseFolderDto>
{
    public async Task<KnowledgeBaseFolderDto> Handle(MoveFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);
        var folder = KnowledgeBaseFolderGuard.EnsureBelongsTo(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), request.KnowledgeBaseId);

        var newParentDepth = 0;
        if (request.NewParentFolderId is { } newParentFolderId)
        {
            if (await folderRepository.IsSameOrDescendantAsync(newParentFolderId, folder.Id, cancellationToken))
            {
                throw new DomainRuleViolationException("A folder cannot be moved into itself or one of its own subfolders.");
            }

            var newParent = KnowledgeBaseFolderGuard.EnsureBelongsTo(
                await folderRepository.GetByIdAsync(newParentFolderId, cancellationToken), request.KnowledgeBaseId);
            newParentDepth = newParent.Depth;
        }

        folder.MoveTo(request.NewParentFolderId, newParentDepth, folderOptions.Value.MaxNestingDepth, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseFolderDto.FromEntity(folder);
    }
}
