using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ReopenIncident;

/// <summary>specs/074 FR-024 — Resolved → Open; refused while a newer incident holds the same cause.</summary>
public sealed record ReopenIncidentCommand(Guid IncidentId) : IRequest;
