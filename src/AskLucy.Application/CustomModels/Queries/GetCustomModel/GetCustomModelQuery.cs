using MediatR;

namespace AskLucy.Application.CustomModels.Queries.GetCustomModel;

/// <summary>specs/072 contracts/admin-custom-models.md <c>GET {id}</c>. The summary plus a page of the overwritten-file report (FR-010a).</summary>
public sealed record GetCustomModelQuery(
    Guid Id,
    int OverwrittenPage = 1,
    int OverwrittenPageSize = GetCustomModelQuery.DefaultOverwrittenPageSize) : IRequest<CustomModelDetailDto>
{
    public const int DefaultOverwrittenPageSize = 100;
    public const int MaxOverwrittenPageSize = 500;
}
