namespace AskLucy.Domain.OperationalFailures;

/// <summary>What the user got despite the failure. An input to the severity policy only; never stored.</summary>
public enum OperationalFailureOutcome
{
    Failed,
    DegradedServed,
    RecoveredByRetry,
}
