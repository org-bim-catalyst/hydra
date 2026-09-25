namespace AskLucy.Application.Abstractions;

/// <summary>
/// The ambient correlation id — the Hangfire job's when running inside a job, otherwise the HTTP
/// request's, otherwise <see langword="null"/> (specs/074 research D6). Matches the
/// <c>CorrelationId</c> property on the server log lines of the same request or job.
/// </summary>
public interface ICorrelationIdAccessor
{
    string? Current { get; }
}
