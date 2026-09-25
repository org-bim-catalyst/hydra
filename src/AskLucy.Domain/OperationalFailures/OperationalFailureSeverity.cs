namespace AskLucy.Domain.OperationalFailures;

/// <summary>Ordered so that the numerically highest value is the most severe.</summary>
public enum OperationalFailureSeverity
{
    Warning = 1,
    Error = 2,
    Critical = 3,
}
