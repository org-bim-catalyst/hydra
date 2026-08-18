using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListCategories;

public sealed class ListCategoriesQueryHandler(IPromptCategoryRepository repository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListCategoriesQuery, IReadOnlyList<PromptCategoryDto>>
{
    public async Task<IReadOnlyList<PromptCategoryDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var categories = await repository.ListPredefinedAndCustomForOwnerAsync(userId, cancellationToken);
        return [.. categories.Select(PromptCategoryDto.FromEntity)];
    }
}
