using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListRelatedIncidents;

/// <summary>specs/074 FR-026b — the other unresolved incidents sharing this incident's root cause.</summary>
public sealed record ListRelatedIncidentsQuery(Guid IncidentId, int Page = 1, int PageSize = 25)
    : IRequest<PagedResult<IncidentSummaryDto>>;
