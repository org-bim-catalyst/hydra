using System.Collections.Concurrent;
using System.Diagnostics;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Mcp.Resilience;

/// <summary>Thrown when a per-server circuit is open — the caller (<c>McpToolAdapter</c>) records this as a failed call without attempting the underlying MCP request.</summary>
public sealed class McpCircuitOpenException(Guid mcpServerId) : Exception($"The circuit for MCP server {mcpServerId} is open after repeated consecutive failures.")
{
    public Guid McpServerId { get; } = mcpServerId;
}

/// <summary>
/// Retry-with-backoff and a per-server circuit breaker for outbound MCP calls (spec.md FR-037/
/// FR-054, research.md Decision 11) — hand-rolled, not Polly (no such dependency exists anywhere
/// in this codebase). Retry is applied only when the caller marks an operation idempotent/safe-to-
/// retry (health checks, capability discovery, resource reads) — never a tool call whose
/// success/failure state is ambiguous after a dropped connection (FR-054, spec.md Edge Case).
/// The circuit closes again on the next successful call, typically a scheduled health-check
/// probe (research.md Decision 10) calling <see cref="RecordSuccess"/>.
/// </summary>
public sealed class McpConnectionResiliencePolicy(IOptions<McpRuntimeOptions> options, ILogger<McpConnectionResiliencePolicy> logger)
{
    private readonly ConcurrentDictionary<Guid, int> _consecutiveFailures = new();

    public bool IsCircuitOpen(Guid mcpServerId) =>
        _consecutiveFailures.TryGetValue(mcpServerId, out var count) && count >= options.Value.CircuitBreakerFailureThreshold;

    public void RecordSuccess(Guid mcpServerId) => _consecutiveFailures.TryRemove(mcpServerId, out _);

    public void RecordFailure(Guid mcpServerId) => _consecutiveFailures.AddOrUpdate(mcpServerId, 1, (_, current) => current + 1);

    /// <summary>
    /// Executes <paramref name="operation"/>, retrying with exponential backoff only when
    /// <paramref name="isIdempotent"/> is <see langword="true"/>, up to <see cref="McpRuntimeOptions.MaxRetries"/>.
    /// Throws <see cref="McpCircuitOpenException"/> immediately, without attempting the call, when
    /// the server's circuit is already open.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Guid mcpServerId, bool isIdempotent, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (IsCircuitOpen(mcpServerId))
        {
            McpConnectionResiliencePolicyLog.CircuitOpen(logger, mcpServerId);
            throw new McpCircuitOpenException(mcpServerId);
        }

        var attempt = 0;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var result = await operation(cancellationToken);
                RecordSuccess(mcpServerId);
                // FR-057 — tool-call/connection latency, discoverable through the platform's
                // existing observability capability (structured Serilog logging, constitution §14).
                McpConnectionResiliencePolicyLog.CallSucceeded(logger, mcpServerId, stopwatch.ElapsedMilliseconds, attempt + 1);
                return result;
            }
            catch (Exception ex) when (isIdempotent && attempt < options.Value.MaxRetries)
            {
                attempt++;
                RecordFailure(mcpServerId);
                McpConnectionResiliencePolicyLog.CallFailedRetrying(logger, ex, mcpServerId, attempt);
                var backoff = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                await Task.Delay(backoff, cancellationToken);
            }
            catch (Exception ex)
            {
                RecordFailure(mcpServerId);
                McpConnectionResiliencePolicyLog.CallFailedFinal(logger, ex, mcpServerId, stopwatch.ElapsedMilliseconds, attempt + 1);
                throw;
            }
        }
    }
}

/// <summary>CA1848 — LoggerMessage delegates for <see cref="McpConnectionResiliencePolicy"/>'s hot retry/circuit-breaker path.</summary>
internal static partial class McpConnectionResiliencePolicyLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MCP circuit open for server {McpServerId} — call rejected without attempting the request")]
    public static partial void CircuitOpen(ILogger logger, Guid mcpServerId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "MCP call to server {McpServerId} succeeded in {ElapsedMilliseconds}ms after {Attempts} attempt(s)")]
    public static partial void CallSucceeded(ILogger logger, Guid mcpServerId, long elapsedMilliseconds, int attempts);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "MCP call to server {McpServerId} failed on attempt {Attempt}; retrying")]
    public static partial void CallFailedRetrying(ILogger logger, Exception exception, Guid mcpServerId, int attempt);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "MCP call to server {McpServerId} failed after {ElapsedMilliseconds}ms, {Attempts} attempt(s)")]
    public static partial void CallFailedFinal(ILogger logger, Exception exception, Guid mcpServerId, long elapsedMilliseconds, int attempts);
}
