using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListTags;

public sealed class ListTagsQueryHandler(
    IKnowledgeBaseRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListTagsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(ListTagsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await repository.ListDistinctTagValuesAsync(userId, request.Prefix, cancellationToken);
    }
}
