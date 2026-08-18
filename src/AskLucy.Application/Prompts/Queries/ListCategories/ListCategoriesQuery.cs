using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListCategories;

public sealed record ListCategoriesQuery : IRequest<IReadOnlyList<PromptCategoryDto>>;
