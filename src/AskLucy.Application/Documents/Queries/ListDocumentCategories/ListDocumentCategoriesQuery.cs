using MediatR;

namespace AskLucy.Application.Documents.Queries.ListDocumentCategories;

public sealed record DocumentCategoryDto(Guid Id, string Name, bool IsSystemDefined);

/// <summary>The classification taxonomy, for the classification-override picker (US3) and category filter (US4) — not a user-scoped resource (categories are shared/global, data-model.md).</summary>
public sealed record ListDocumentCategoriesQuery : IRequest<IReadOnlyList<DocumentCategoryDto>>;
