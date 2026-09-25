namespace AskLucy.Domain.OperationalFailures;

/// <summary>Outcome of a triage transition: a no-op is reported, not thrown, so bulk actions can skip it (FR-026b).</summary>
public enum IncidentTransitionResult
{
    Applied,
    AlreadyInState,
}
