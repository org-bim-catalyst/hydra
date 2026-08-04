using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListCategories;

public sealed class ListCategoriesQueryHandler(
    IKnowledgeBaseCategoryRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListCategoriesQuery, IReadOnlyList<KnowledgeBaseCategoryDto>>
{
    public async Task<IReadOnlyList<KnowledgeBaseCategoryDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var categories = await repository.ListPredefinedAndOwnedAsync(userId, cancellationToken);

        return [.. categories.Select(KnowledgeBaseCategoryDto.FromEntity)];
    }
}
