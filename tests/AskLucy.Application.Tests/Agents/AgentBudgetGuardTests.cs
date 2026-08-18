using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class AgentBudgetGuardTests
{
    private static readonly AgentRuntimeOptions Defaults = new()
    {
        DefaultMaxSteps = 25,
        DefaultMaxExecutionDurationSeconds = 900,
        DefaultMaxTokens = 200_000,
        DefaultMaxCost = 5.00m,
        DefaultMaxToolCalls = 50,
        DefaultMaxRetries = 3,
    };

    private static AgentBudgetGuard CreateGuard() => new(Microsoft.Extensions.Options.Options.Create(Defaults));

    [Fact]
    public void Check_ShouldNotBeExceeded_WhenEveryValueIsWithinDefaults()
    {
        var result = CreateGuard().Check(AgentExecutionPolicy.Empty, DateTime.UtcNow, stepCount: 1, toolCallCount: 0, retryCount: 0, inputTokens: 100, outputTokens: 50, estimatedCost: 0.01m);

        result.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public void Check_ShouldReportMaxSteps_WhenAgentPolicyOverrideIsExceeded()
    {
        var policy = new AgentExecutionPolicy(MaxSteps: 2, null, null, null, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, stepCount: 3, toolCallCount: 0, retryCount: 0, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxSteps);
    }

    [Fact]
    public void Check_ShouldReportMaxExecutionDuration_WhenElapsedTimeExceedsTheDefault()
    {
        var startedAtUtc = DateTime.UtcNow.AddSeconds(-(Defaults.DefaultMaxExecutionDurationSeconds + 10));

        var result = CreateGuard().Check(AgentExecutionPolicy.Empty, startedAtUtc, stepCount: 1, toolCallCount: 0, retryCount: 0, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxExecutionDuration);
    }

    [Fact]
    public void Check_ShouldReportMaxTokens_WhenAgentPolicyOverrideIsExceeded()
    {
        var policy = new AgentExecutionPolicy(null, null, MaxTokens: 100, null, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, stepCount: 1, toolCallCount: 0, retryCount: 0, inputTokens: 80, outputTokens: 30, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxTokens);
    }

    [Fact]
    public void Check_ShouldReportMaxCost_WhenAgentPolicyOverrideIsExceeded()
    {
        var policy = new AgentExecutionPolicy(null, null, null, MaxCost: 1.00m, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, stepCount: 1, toolCallCount: 0, retryCount: 0, null, null, estimatedCost: 1.50m);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxCost);
    }

    [Fact]
    public void Check_ShouldReportMaxToolCalls_WhenAgentPolicyOverrideIsExceeded()
    {
        var policy = new AgentExecutionPolicy(null, null, null, null, MaxToolCalls: 2, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, stepCount: 1, toolCallCount: 3, retryCount: 0, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxToolCalls);
    }

    [Fact]
    public void Check_ShouldReportMaxRetries_WhenAgentPolicyOverrideIsExceeded()
    {
        var policy = new AgentExecutionPolicy(null, null, null, null, null, MaxRetries: 1);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, stepCount: 1, toolCallCount: 0, retryCount: 2, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(AgentBudgetLimitType.MaxRetries);
    }
}
