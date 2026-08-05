using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Documents.Queries.ListTags;

public sealed class ListTagsQueryHandler(
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListTagsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(ListTagsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var tags = await documentRepository.ListTagsByOwnerAsync(userId, cancellationToken);
        return tags.Select(t => t.Name).ToList();
    }
}
