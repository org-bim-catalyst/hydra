using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetModelDeprecations;

/// <summary>specs/069 contracts §3: cursor-paged deprecation records, newest first.</summary>
public sealed record ListModelDeprecationsQuery(
    Guid? ProviderId = null,
    bool AwaitingValidation = false,
    bool OpenFlags = false,
    Guid? Cursor = null,
    int PageSize = 20) : IRequest<ModelDeprecationPageDto>;

public sealed class ListModelDeprecationsQueryHandler(IModelDeprecationQueryRepository deprecations)
    : IRequestHandler<ListModelDeprecationsQuery, ModelDeprecationPageDto>
{
    public Task<ModelDeprecationPageDto> Handle(ListModelDeprecationsQuery request, CancellationToken cancellationToken) =>
        deprecations.ListAsync(
            request.ProviderId,
            request.AwaitingValidation,
            request.OpenFlags,
            request.Cursor,
            Math.Clamp(request.PageSize, 1, 100),
            cancellationToken);
}

public sealed record GetModelDeprecationQuery(Guid DeprecationId) : IRequest<ModelDeprecationDetailDto>;

public sealed class GetModelDeprecationQueryHandler(IModelDeprecationQueryRepository deprecations)
    : IRequestHandler<GetModelDeprecationQuery, ModelDeprecationDetailDto>
{
    public async Task<ModelDeprecationDetailDto> Handle(GetModelDeprecationQuery request, CancellationToken cancellationToken) =>
        await deprecations.GetAsync(request.DeprecationId, cancellationToken)
        ?? throw new KeyNotFoundException("Deprecation not found.");
}
