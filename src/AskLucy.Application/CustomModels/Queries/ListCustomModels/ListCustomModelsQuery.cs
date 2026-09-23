using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.CustomModels.Queries.ListCustomModels;

/// <summary>specs/072 contracts/admin-custom-models.md <c>GET api/v1/admin/custom-models</c>. Newest first.</summary>
public sealed record ListCustomModelsQuery(int Page = 1, int PageSize = ListCustomModelsQuery.DefaultPageSize) : IRequest<PagedResult<CustomModelSummaryDto>>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
}
