using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.CustomModels.Queries.GetCustomModel;

public sealed class GetCustomModelQueryHandler(
    ICustomModelRepository customModels,
    CustomModelSummaryBuilder summaries) : IRequestHandler<GetCustomModelQuery, CustomModelDetailDto>
{
    public async Task<CustomModelDetailDto> Handle(GetCustomModelQuery request, CancellationToken cancellationToken)
    {
        var model = await customModels.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Custom model not found.");

        var page = Math.Max(1, request.OverwrittenPage);
        var pageSize = Math.Clamp(request.OverwrittenPageSize, 1, GetCustomModelQuery.MaxOverwrittenPageSize);
        var (files, totalCount) = await customModels.GetOverwrittenFilesAsync(model.Id, page, pageSize, cancellationToken);

        var summary = await summaries.BuildAsync(model, cancellationToken);
        return new CustomModelDetailDto(
            summary,
            new PagedResult<OverwrittenFileDto>(files.Select(f => f.ToDto()).ToList(), totalCount, page, pageSize));
    }
}
