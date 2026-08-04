using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateFolder;

public sealed class CreateFolderCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IOptions<KnowledgeBaseFolderOptions> folderOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateFolderCommand, KnowledgeBaseFolderDto>
{
    public async Task<KnowledgeBaseFolderDto> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);

        var parentDepth = 0;
        if (request.ParentFolderId is { } parentFolderId)
        {
            var parent = await folderRepository.GetByIdAsync(parentFolderId, cancellationToken)
                ?? throw new DomainRuleViolationException("The parent folder does not exist.");
            if (parent.KnowledgeBaseId != request.KnowledgeBaseId)
            {
                throw new DomainRuleViolationException("The parent folder does not belong to this knowledge base.");
            }

            parentDepth = parent.Depth;
        }

        var folder = KnowledgeBaseFolder.Create(
            request.KnowledgeBaseId, request.Name, request.ParentFolderId, parentDepth, folderOptions.Value.MaxNestingDepth, userId);
        folderRepository.Add(folder);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseFolderDto.FromEntity(folder);
    }
}
