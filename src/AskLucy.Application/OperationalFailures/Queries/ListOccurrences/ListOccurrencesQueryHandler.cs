using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListOccurrences;

public sealed class ListOccurrencesQueryHandler(IOperationalFailureStore store, OperationalFailureReadModelBuilder readModels)
    : IRequestHandler<ListOccurrencesQuery, PagedResult<OccurrenceDto>>
{
    public async Task<PagedResult<OccurrenceDto>> Handle(ListOccurrencesQuery request, CancellationToken cancellationToken)
    {
        _ = await store.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Incident {request.IncidentId} was not found.");

        var (occurrences, total) = await store.ListOccurrencesAsync(request.IncidentId, request.Page, request.PageSize, cancellationToken);
        var items = await readModels.OccurrencesAsync(occurrences, cancellationToken);
        return new PagedResult<OccurrenceDto>(items, total, request.Page, request.PageSize);
    }
}
