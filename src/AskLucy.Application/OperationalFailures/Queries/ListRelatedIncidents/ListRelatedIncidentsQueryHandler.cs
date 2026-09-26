using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListRelatedIncidents;

public sealed class ListRelatedIncidentsQueryHandler(IOperationalFailureStore store, OperationalFailureReadModelBuilder readModels)
    : IRequestHandler<ListRelatedIncidentsQuery, PagedResult<IncidentSummaryDto>>
{
    public async Task<PagedResult<IncidentSummaryDto>> Handle(ListRelatedIncidentsQuery request, CancellationToken cancellationToken)
    {
        var (incidents, total) = await store.ListRelatedAsync(request.IncidentId, request.Page, request.PageSize, cancellationToken)
            ?? throw new KeyNotFoundException($"Incident {request.IncidentId} was not found.");

        var items = await readModels.SummariesAsync(incidents, cancellationToken);
        return new PagedResult<IncidentSummaryDto>(items, total, request.Page, request.PageSize);
    }
}
