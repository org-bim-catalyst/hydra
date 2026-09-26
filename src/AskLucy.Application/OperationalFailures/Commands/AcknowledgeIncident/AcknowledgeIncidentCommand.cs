using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeIncident;

/// <summary>specs/074 FR-024 — Open → Acknowledged, recording who and when.</summary>
public sealed record AcknowledgeIncidentCommand(Guid IncidentId) : IRequest;
