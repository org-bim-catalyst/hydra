using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListCategories;

public sealed record ListCategoriesQuery : IRequest<IReadOnlyList<KnowledgeBaseCategoryDto>>;
