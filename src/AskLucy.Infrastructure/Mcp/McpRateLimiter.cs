using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>
/// In-process rate/concurrency limiting for outbound MCP calls (spec.md FR-052/FR-053,
/// research.md Decision 12) built directly on <see cref="System.Threading.RateLimiting"/>
/// primitives — distinct from the ASP.NET Core inbound-request <c>RateLimiting</c> middleware,
/// which only governs the admin/browsing REST endpoints. Registered as a singleton so limits are
/// enforced across the whole process, not reset per scope.
/// </summary>
public sealed class McpRateLimiter(IOptions<McpRuntimeOptions> options, ILogger<McpRateLimiter> logger) : IMcpRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _perKeyLimiters = new();
    private readonly ConcurrentDictionary<Guid, RateLimiter> _perServerConcurrencyLimiters = new();

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(McpRateLimitKey key, CancellationToken cancellationToken = default)
    {
        var rateLimiter = _perKeyLimiters.GetOrAdd(RateLimitKeyString(key), _ => CreateRateWindowLimiter());
        var rateLease = await rateLimiter.AcquireAsync(1, cancellationToken);
        if (!rateLease.IsAcquired)
        {
            rateLease.Dispose();
            // FR-057 — rate-limit event, discoverable through the platform's existing
            // observability capability (structured Serilog logging, constitution §14).
            logger.LogWarning(
                "MCP rate limit exceeded for server {McpServerId}, tool {ToolName}, user {UserId}, agent {AgentId}",
                key.McpServerId, key.ToolName, key.UserId, key.AgentId);
            return null;
        }

        var concurrencyLimiter = _perServerConcurrencyLimiters.GetOrAdd(key.McpServerId, _ => CreateConcurrencyLimiter());
        var concurrencyLease = await concurrencyLimiter.AcquireAsync(1, cancellationToken);
        if (!concurrencyLease.IsAcquired)
        {
            concurrencyLease.Dispose();
            rateLease.Dispose();
            logger.LogWarning(
                "MCP concurrency limit exceeded for server {McpServerId} (tool {ToolName}, user {UserId}, agent {AgentId})",
                key.McpServerId, key.ToolName, key.UserId, key.AgentId);
            return null;
        }

        return new CompositeLease(rateLease, concurrencyLease);
    }

    private RateLimiter CreateRateWindowLimiter() => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
    {
        PermitLimit = options.Value.MaxRequestsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    });

    private RateLimiter CreateConcurrencyLimiter() => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = options.Value.MaxConcurrentRequestsPerServer,
        QueueLimit = 0,
    });

    private static string RateLimitKeyString(McpRateLimitKey key) => $"{key.McpServerId}|{key.ToolName}|{key.UserId}|{key.AgentId}";

    public void Dispose()
    {
        foreach (var limiter in _perKeyLimiters.Values)
        {
            limiter.Dispose();
        }

        foreach (var limiter in _perServerConcurrencyLimiters.Values)
        {
            limiter.Dispose();
        }
    }

    private sealed class CompositeLease(RateLimitLease rateLease, RateLimitLease concurrencyLease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            concurrencyLease.Dispose();
            rateLease.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
