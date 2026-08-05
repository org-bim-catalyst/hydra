using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Documents.Queries.ListDocumentCategories;

public sealed class ListDocumentCategoriesQueryHandler(IDocumentRepository documentRepository)
    : IRequestHandler<ListDocumentCategoriesQuery, IReadOnlyList<DocumentCategoryDto>>
{
    public async Task<IReadOnlyList<DocumentCategoryDto>> Handle(ListDocumentCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await documentRepository.ListCategoriesAsync(cancellationToken);
        return categories.Select(c => new DocumentCategoryDto(c.Id, c.Name, c.IsSystemDefined)).ToList();
    }
}
