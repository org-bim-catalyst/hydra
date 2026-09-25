namespace AskLucy.Infrastructure.OperationalFailures;

/// <summary>
/// The correlation id of the background job running on this async flow (specs/074 research D11).
/// Set by the Hangfire job filter for the duration of a job and read first by the correlation-id
/// accessor, so a job's failures, audit rows and log lines share one id. Outside a job it is null,
/// and the accessor falls back to the request's id.
/// </summary>
public static class JobCorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentJob = new();

    public static string? Current
    {
        get => CurrentJob.Value;
        set => CurrentJob.Value = value;
    }
}
