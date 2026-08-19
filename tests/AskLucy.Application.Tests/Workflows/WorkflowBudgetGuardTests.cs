using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>T113 — mirrors <c>AgentBudgetGuardTests</c> exactly (research.md Decision 10).</summary>
public sealed class WorkflowBudgetGuardTests
{
    private static readonly WorkflowRuntimeOptions Defaults = new()
    {
        DefaultMaxNodeCount = 100,
        DefaultMaxExecutionDurationSeconds = 1800,
        DefaultMaxTokens = 200_000,
        DefaultMaxCost = 5.00m,
        DefaultMaxToolCalls = 50,
        DefaultMaxParallelNodes = 10,
        DefaultMaxLoopIterations = 100,
    };

    private static WorkflowBudgetGuard CreateGuard() => new(Microsoft.Extensions.Options.Options.Create(Defaults));

    [Fact]
    public void Check_ShouldNotBeExceeded_WhenEveryValueIsWithinDefaults()
    {
        var result = CreateGuard().Check(WorkflowExecutionPolicy.Empty, DateTime.UtcNow, nodeCount: 1, toolCallCount: 0, inputTokens: 100, outputTokens: 50, estimatedCost: 0.01m);

        result.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public void Check_ShouldReportMaxNodeCount_WhenPolicyOverrideIsExceeded()
    {
        var policy = new WorkflowExecutionPolicy(MaxNodeCount: 2, null, null, null, null, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, nodeCount: 3, toolCallCount: 0, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxNodeCount);
    }

    [Fact]
    public void Check_ShouldReportMaxExecutionDuration_WhenElapsedTimeExceedsTheDefault()
    {
        var startedAtUtc = DateTime.UtcNow.AddSeconds(-(Defaults.DefaultMaxExecutionDurationSeconds + 10));

        var result = CreateGuard().Check(WorkflowExecutionPolicy.Empty, startedAtUtc, nodeCount: 1, toolCallCount: 0, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxExecutionDuration);
    }

    [Fact]
    public void Check_ShouldReportMaxTokens_WhenPolicyOverrideIsExceeded()
    {
        var policy = new WorkflowExecutionPolicy(null, null, MaxTokens: 100, null, null, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, nodeCount: 1, toolCallCount: 0, inputTokens: 80, outputTokens: 30, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxTokens);
    }

    [Fact]
    public void Check_ShouldReportMaxCost_WhenPolicyOverrideIsExceeded()
    {
        var policy = new WorkflowExecutionPolicy(null, null, null, MaxCost: 1.00m, null, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, nodeCount: 1, toolCallCount: 0, null, null, estimatedCost: 1.50m);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxCost);
    }

    [Fact]
    public void Check_ShouldReportMaxToolCalls_WhenPolicyOverrideIsExceeded()
    {
        var policy = new WorkflowExecutionPolicy(null, null, null, null, MaxToolCalls: 2, null, null);

        var result = CreateGuard().Check(policy, DateTime.UtcNow, nodeCount: 1, toolCallCount: 3, null, null, null);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxToolCalls);
    }

    [Fact]
    public void ResolveMaxParallelNodes_ShouldFallBackToTheDefault_WhenThePolicyDoesNotOverrideIt()
    {
        CreateGuard().ResolveMaxParallelNodes(WorkflowExecutionPolicy.Empty).Should().Be(Defaults.DefaultMaxParallelNodes);
    }

    [Fact]
    public void ResolveMaxParallelNodes_ShouldUseThePolicyOverride_WhenSet()
    {
        var policy = new WorkflowExecutionPolicy(null, null, null, null, null, MaxParallelNodes: 3, null);

        CreateGuard().ResolveMaxParallelNodes(policy).Should().Be(3);
    }

    [Fact]
    public void CheckLoopIteration_ShouldNotBeExceeded_BelowTheNodeDeclaredMax()
    {
        var result = CreateGuard().CheckLoopIteration(WorkflowExecutionPolicy.Empty, nodeDeclaredMaxIterations: 5, currentIterationCount: 3);

        result.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public void CheckLoopIteration_ShouldBeExceeded_AtTheNodeDeclaredMax()
    {
        var result = CreateGuard().CheckLoopIteration(WorkflowExecutionPolicy.Empty, nodeDeclaredMaxIterations: 5, currentIterationCount: 5);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxLoopIterations);
    }

    [Fact]
    public void CheckLoopIteration_ShouldBeCappedByThePolicyCeiling_EvenWhenTheNodeDeclaresAHigherMax()
    {
        var policy = new WorkflowExecutionPolicy(null, null, null, null, null, null, MaxLoopIterations: 3);

        var result = CreateGuard().CheckLoopIteration(policy, nodeDeclaredMaxIterations: 1000, currentIterationCount: 3);

        result.IsExceeded.Should().BeTrue();
        result.LimitType.Should().Be(WorkflowBudgetLimitType.MaxLoopIterations);
    }
}
