namespace AskLucy.Application.Abstractions;

/// <summary>The composite key every MCP rate/concurrency limit is scoped to (spec.md FR-052/FR-053, research.md Decision 12).</summary>
public sealed record McpRateLimitKey(Guid McpServerId, string ToolName, string UserId, Guid AgentId);

/// <summary>
/// In-process rate/concurrency limiting for outbound MCP calls (research.md Decision 12) — built on
/// <c>System.Threading.RateLimiting</c> primitives directly, distinct from the ASP.NET Core
/// inbound-request <c>RateLimiting</c> middleware, which only governs the admin/browsing REST
/// endpoints. This traffic originates from the Hangfire execution runner, never the HTTP pipeline.
/// </summary>
public interface IMcpRateLimiter
{
    /// <summary>
    /// Attempts to acquire both a rate-limit slot (per <see cref="McpRateLimitKey"/>, FR-053) and a
    /// concurrency slot (per <c>McpServerId</c> only, FR-052) for one outbound call. Returns
    /// <see langword="false"/> — never throws — when either limit is exhausted; the caller records
    /// this as a failed <c>AgentToolCall</c> with <c>McpFailureCategory.RateLimit</c> rather than
    /// retrying or queuing indefinitely.
    /// </summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(McpRateLimitKey key, CancellationToken cancellationToken = default);
}
