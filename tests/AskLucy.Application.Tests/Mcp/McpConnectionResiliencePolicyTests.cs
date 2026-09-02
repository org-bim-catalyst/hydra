using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class McpConnectionResiliencePolicyTests
{
    private static McpConnectionResiliencePolicy CreatePolicy(int maxRetries = 2, int circuitBreakerFailureThreshold = 3) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = maxRetries, CircuitBreakerFailureThreshold = circuitBreakerFailureThreshold }),
            Substitute.For<ILogger<McpConnectionResiliencePolicy>>());

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResult_OnFirstSuccess()
    {
        var policy = CreatePolicy();

        var result = await policy.ExecuteAsync(Guid.NewGuid(), isIdempotent: true, _ => Task.FromResult(42), TestContext.Current.CancellationToken);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotRetry_WhenNotMarkedIdempotent()
    {
        var policy = CreatePolicy();
        var attempts = 0;

        var act = async () => await policy.ExecuteAsync<int>(Guid.NewGuid(), isIdempotent: false, _ =>
        {
            attempts++;
            throw new InvalidOperationException("ambiguous outcome");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryUpToMaxRetries_WhenIdempotent()
    {
        var policy = CreatePolicy(maxRetries: 2);
        var attempts = 0;

        var act = async () => await policy.ExecuteAsync<int>(Guid.NewGuid(), isIdempotent: true, _ =>
        {
            attempts++;
            throw new InvalidOperationException("transient");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(3); // initial attempt + 2 retries
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceedAfterTransientFailure_WhenIdempotent()
    {
        var policy = CreatePolicy(maxRetries: 3);
        var attempts = 0;

        var result = await policy.ExecuteAsync(Guid.NewGuid(), isIdempotent: true, _ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.FromResult(99);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(99);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Circuit_ShouldOpen_AfterConsecutiveFailuresReachThreshold()
    {
        var policy = CreatePolicy(maxRetries: 0, circuitBreakerFailureThreshold: 2);
        var serverId = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
        {
            var act = async () => await policy.ExecuteAsync<int>(serverId, isIdempotent: false, _ => throw new InvalidOperationException());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        policy.IsCircuitOpen(serverId).Should().BeTrue();

        var openCircuitAct = async () => await policy.ExecuteAsync<int>(serverId, isIdempotent: false, _ => Task.FromResult(1));
        await openCircuitAct.Should().ThrowAsync<McpCircuitOpenException>();
    }

    [Fact]
    public void RecordSuccess_ShouldCloseCircuit()
    {
        var policy = CreatePolicy(circuitBreakerFailureThreshold: 1);
        var serverId = Guid.NewGuid();

        policy.RecordFailure(serverId);
        policy.IsCircuitOpen(serverId).Should().BeTrue();

        policy.RecordSuccess(serverId);
        policy.IsCircuitOpen(serverId).Should().BeFalse();
    }
}
