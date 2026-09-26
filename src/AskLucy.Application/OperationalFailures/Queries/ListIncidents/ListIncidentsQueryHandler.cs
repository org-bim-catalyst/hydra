using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListIncidents;

public sealed class ListIncidentsQueryHandler(
    IOperationalFailureStore store, OperationalFailureReadModelBuilder readModels, TimeProvider timeProvider)
    : IRequestHandler<ListIncidentsQuery, PagedResult<IncidentSummaryDto>>
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

    public async Task<PagedResult<IncidentSummaryDto>> Handle(ListIncidentsQuery request, CancellationToken cancellationToken)
    {
        var to = request.ToUtc ?? timeProvider.GetUtcNow().UtcDateTime;
        var from = request.FromUtc ?? to - DefaultRange;

        var filter = new IncidentFilter(
            from,
            to,
            request.State ?? IncidentStateFilter.Unresolved,
            request.Severity,
            request.Engine,
            request.Provider,
            request.Kind,
            request.UserId);

        var (incidents, total) = await store.ListIncidentsAsync(filter, request.Page, request.PageSize, cancellationToken);
        var items = await readModels.SummariesAsync(incidents, cancellationToken);
        return new PagedResult<IncidentSummaryDto>(items, total, request.Page, request.PageSize);
    }
}
