using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveIncident;

/// <summary>specs/074 FR-024 — Open or Acknowledged → Resolved, with an optional note.</summary>
public sealed record ResolveIncidentCommand(Guid IncidentId, string? Note) : IRequest;
