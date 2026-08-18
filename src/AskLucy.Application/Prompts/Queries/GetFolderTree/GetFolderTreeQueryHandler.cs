using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetFolderTree;

public sealed class GetFolderTreeQueryHandler(IPromptFolderRepository folderRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetFolderTreeQuery, IReadOnlyList<PromptFolderDto>>
{
    public async Task<IReadOnlyList<PromptFolderDto>> Handle(GetFolderTreeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var folders = await folderRepository.GetTreeForOwnerAsync(userId, cancellationToken);
        return [.. folders.Select(PromptFolderDto.FromEntity)];
    }
}
