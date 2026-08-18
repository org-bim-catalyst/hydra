using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListTags;

public sealed class ListTagsQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListTagsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(ListTagsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await promptRepository.ListDistinctTagValuesAsync(userId, cancellationToken);
    }
}
