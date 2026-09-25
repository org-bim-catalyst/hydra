namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>
/// Exactly one occurrence per failure (research D11): a site that records a failure and then lets
/// the exception propagate marks it, and the boundary recorders (ProblemDetailsMiddleware, the
/// Hangfire filter) skip marked exceptions.
/// </summary>
public static class OperationalFailureMarkers
{
    public const string Recorded = "AskLucy.OperationalFailureRecorded";

    public static void MarkOperationalFailureRecorded(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[Recorded] = true;
    }

    public static bool IsOperationalFailureRecorded(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[Recorded] is true;
    }
}
