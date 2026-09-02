using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>FR-057 — connection/tool-call latency and failure/rate-limit events are discoverable through the platform's existing observability capability (structured Serilog logging).</summary>
public sealed class McpObservabilityTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldLogInformation_OnSuccess()
    {
        var logger = Substitute.For<ILogger<McpConnectionResiliencePolicy>>();
        // Required, not incidental. The policy logs through source-generated [LoggerMessage]
        // methods, and every one of those checks IsEnabled(level) before doing any work. A bare
        // NSubstitute mock returns false for bool, so the log call was skipped entirely and
        // Received().Log(...) below could never match — these three assertions had been failing
        // since the MCP feature landed, for a reason that had nothing to do with the policy.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var policy = new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), logger);

        await policy.ExecuteAsync(Guid.NewGuid(), isIdempotent: true, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        // Mock-verification call (NSubstitute Received()), not a real logging invocation — CA1873's
        // "expensive eager evaluation" concern doesn't apply since there is no disabled-logger fast path here.
#pragma warning disable CA1873
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("succeeded", StringComparison.Ordinal)),
            null,
            Arg.Any<Func<object, Exception?, string>>());
#pragma warning restore CA1873
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogWarning_OnFailure()
    {
        var logger = Substitute.For<ILogger<McpConnectionResiliencePolicy>>();
        // Required, not incidental. The policy logs through source-generated [LoggerMessage]
        // methods, and every one of those checks IsEnabled(level) before doing any work. A bare
        // NSubstitute mock returns false for bool, so the log call was skipped entirely and
        // Received().Log(...) below could never match — these three assertions had been failing
        // since the MCP feature landed, for a reason that had nothing to do with the policy.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var policy = new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), logger);

        var act = async () => await policy.ExecuteAsync<int>(Guid.NewGuid(), isIdempotent: false, _ => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogWarning_WhenCircuitIsOpen()
    {
        var logger = Substitute.For<ILogger<McpConnectionResiliencePolicy>>();
        // Required, not incidental. The policy logs through source-generated [LoggerMessage]
        // methods, and every one of those checks IsEnabled(level) before doing any work. A bare
        // NSubstitute mock returns false for bool, so the log call was skipped entirely and
        // Received().Log(...) below could never match — these three assertions had been failing
        // since the MCP feature landed, for a reason that had nothing to do with the policy.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var policy = new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { CircuitBreakerFailureThreshold = 1 }), logger);
        var serverId = Guid.NewGuid();
        policy.RecordFailure(serverId);

        var act = async () => await policy.ExecuteAsync<int>(serverId, isIdempotent: false, _ => Task.FromResult(1));
        await Assert.ThrowsAsync<McpCircuitOpenException>(act);

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("circuit", StringComparison.OrdinalIgnoreCase)),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
