namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// FR-009 / research D5: severity is derived from the engine, kind and outcome, never chosen by
/// the caller.
/// </summary>
public static class OperationalFailureSeverityPolicy
{
    public static OperationalFailureSeverity Classify(
        OperationalFailureEngine engine,
        OperationalFailureKind kind,
        OperationalFailureOutcome outcome)
    {
        if (engine == OperationalFailureEngine.Access)
        {
            return OperationalFailureSeverity.Warning;
        }

        // Needs an administrator and affects everyone on the provider — even when a fallback
        // served this user (a rejected ElevenLabs key is Critical although the fallback spoke).
        if (IsCritical(kind))
        {
            return OperationalFailureSeverity.Critical;
        }

        if (kind == OperationalFailureKind.RateLimited ||
            outcome is OperationalFailureOutcome.DegradedServed or OperationalFailureOutcome.RecoveredByRetry)
        {
            return OperationalFailureSeverity.Warning;
        }

        return OperationalFailureSeverity.Error;
    }

    public static bool IsCritical(OperationalFailureKind kind) => kind is
        OperationalFailureKind.CredentialRejected or
        OperationalFailureKind.CredentialUnreadable or
        OperationalFailureKind.NotConfigured or
        OperationalFailureKind.QuotaExhausted or
        OperationalFailureKind.UsageRestricted;
}
