namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// specs/074 FR-024 — a triage transition whose precondition no longer holds: someone else moved
/// the incident first, or a reopen collided with a newer incident for the same cause
/// (<see cref="NewerIncidentId"/>). Maps to 409 <c>incident-conflict</c>.
/// </summary>
public sealed class IncidentConflictException(string message, Guid? newerIncidentId = null) : Exception(message)
{
    public Guid? NewerIncidentId { get; } = newerIncidentId;
}
