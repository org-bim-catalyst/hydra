using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Mcp;

public sealed class McpRateLimiterTests
{
    private static McpRateLimiter CreateLimiter(int maxRequestsPerMinute = 2, int maxConcurrentRequestsPerServer = 1) =>
        new(
            Options.Create(new McpRuntimeOptions
            {
                MaxRequestsPerMinute = maxRequestsPerMinute,
                MaxConcurrentRequestsPerServer = maxConcurrentRequestsPerServer,
            }),
            Substitute.For<ILogger<McpRateLimiter>>());

    private static McpRateLimitKey Key(Guid? serverId = null, string tool = "search", string user = "user-1", Guid? agentId = null) =>
        new(serverId ?? Guid.NewGuid(), tool, user, agentId ?? Guid.NewGuid());

    [Fact]
    public async Task TryAcquireAsync_ShouldSucceed_WithinLimit()
    {
        using var limiter = CreateLimiter();

        var lease = await limiter.TryAcquireAsync(Key());

        lease.Should().NotBeNull();
        if (lease is not null)
        {
            await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReject_WhenRateLimitExceeded_ForSameKey()
    {
        using var limiter = CreateLimiter(maxRequestsPerMinute: 1, maxConcurrentRequestsPerServer: 10);
        var key = Key();

        var first = await limiter.TryAcquireAsync(key);
        var second = await limiter.TryAcquireAsync(key);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldIsolate_DifferentKeys()
    {
        using var limiter = CreateLimiter(maxRequestsPerMinute: 1, maxConcurrentRequestsPerServer: 10);

        var first = await limiter.TryAcquireAsync(Key(user: "user-1"));
        var second = await limiter.TryAcquireAsync(Key(user: "user-2"));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReject_WhenServerConcurrencyLimitExceeded_AcrossDifferentKeys()
    {
        using var limiter = CreateLimiter(maxRequestsPerMinute: 100, maxConcurrentRequestsPerServer: 1);
        var serverId = Guid.NewGuid();

        var first = await limiter.TryAcquireAsync(Key(serverId: serverId, tool: "toolA"));
        var second = await limiter.TryAcquireAsync(Key(serverId: serverId, tool: "toolB"));

        first.Should().NotBeNull();
        second.Should().BeNull();

        if (first is not null)
        {
            await first.DisposeAsync();
        }

        var third = await limiter.TryAcquireAsync(Key(serverId: serverId, tool: "toolC"));
        third.Should().NotBeNull();
    }
}
