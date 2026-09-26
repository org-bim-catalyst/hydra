using AskLucy.Application.Users;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListOccurrences;

/// <summary>specs/074 FR-021 — an incident's stored occurrences, newest first.</summary>
public sealed record ListOccurrencesQuery(Guid IncidentId, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<OccurrenceDto>>;
