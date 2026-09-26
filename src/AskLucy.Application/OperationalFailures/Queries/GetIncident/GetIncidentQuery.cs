using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.GetIncident;

/// <summary>specs/074 FR-013–FR-017 — one incident with its corrective action, provider health and sample users.</summary>
public sealed record GetIncidentQuery(Guid IncidentId) : IRequest<IncidentDetailDto>;
