using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.CustomModels.Queries.ListCustomModels;

public sealed class ListCustomModelsQueryHandler(
    ICustomModelRepository customModels,
    CustomModelSummaryBuilder summaries) : IRequestHandler<ListCustomModelsQuery, PagedResult<CustomModelSummaryDto>>
{
    public async Task<PagedResult<CustomModelSummaryDto>> Handle(ListCustomModelsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, ListCustomModelsQuery.MaxPageSize);

        var (models, totalCount) = await customModels.ListAsync(page, pageSize, cancellationToken);
        var items = await summaries.BuildAsync(models, cancellationToken);

        return new PagedResult<CustomModelSummaryDto>(items, totalCount, page, pageSize);
    }
}
