using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Prompts.Commands.CreateFolder;

public sealed class CreateFolderCommandHandler(
    IPromptFolderRepository folderRepository,
    IOptions<PromptFolderOptions> folderOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateFolderCommand, PromptFolderDto>
{
    public async Task<PromptFolderDto> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var parentDepth = 0;
        if (request.ParentFolderId is { } parentFolderId)
        {
            var parent = await folderRepository.GetByIdForOwnerAsync(parentFolderId, userId, cancellationToken)
                ?? throw new DomainRuleViolationException("The parent folder does not exist.");
            parentDepth = parent.Depth;
        }

        var folder = PromptFolder.Create(userId, request.Name, request.ParentFolderId, parentDepth, folderOptions.Value.MaxNestingDepth, userId);
        folderRepository.Add(folder);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptFolderDto.FromEntity(folder);
    }
}
