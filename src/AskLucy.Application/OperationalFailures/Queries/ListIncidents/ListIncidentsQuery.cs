using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.OperationalFailures;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.ListIncidents;

/// <summary>
/// specs/074 FR-018/FR-019 — the admin incident list. An absent range is the last seven days; an
/// absent state is <see cref="IncidentStateFilter.Unresolved"/>. Values within one
/// multi-valued filter are OR-ed.
/// </summary>
public sealed record ListIncidentsQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    IncidentStateFilter? State = null,
    IReadOnlyList<OperationalFailureSeverity>? Severities = null,
    IReadOnlyList<OperationalFailureEngine>? Engines = null,
    string? Provider = null,
    IReadOnlyList<OperationalFailureKind>? Kinds = null,
    string? UserId = null,
    int Page = 1,
    int PageSize = 25) : IRequest<PagedResult<IncidentSummaryDto>>;
