using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class AgentPolicyEvaluatorTests
{
    private readonly IAgentPolicyRepository _policyRepository = Substitute.For<IAgentPolicyRepository>();

    [Fact]
    public async Task FindMatchAsync_ShouldMatch_WhenConditionsJsonIsNull()
    {
        var policy = AgentPolicy.Create("Always allow", null, "FakeHighRiskTool", conditionsJson: null, "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", """{"action":"anything"}""", CancellationToken.None);

        match.Should().Be(policy);
    }

    [Fact]
    public async Task FindMatchAsync_ShouldReturnNull_WhenNoEnabledPolicyTargetsTheTool()
    {
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", """{"action":"anything"}""", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldMatch_WhenEveryConditionEqualsTheActualInput()
    {
        var policy = AgentPolicy.Create("Read-only allowed", null, "FakeHighRiskTool", """{"action":"read-only"}""", "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", """{"action":"read-only"}""", CancellationToken.None);

        match.Should().Be(policy);
    }

    [Fact]
    public async Task FindMatchAsync_ShouldNotMatch_WhenAConditionValueDiffersFromTheActualInput()
    {
        var policy = AgentPolicy.Create("Read-only allowed", null, "FakeHighRiskTool", """{"action":"read-only"}""", "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", """{"action":"delete"}""", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldNotMatch_WhenAConditionKeyIsMissingFromTheActualInput()
    {
        var policy = AgentPolicy.Create("Read-only allowed", null, "FakeHighRiskTool", """{"action":"read-only"}""", "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", "{}", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldReturnTheFirstMatchingPolicy_WhenSeveralAreEnabled()
    {
        var nonMatching = AgentPolicy.Create("Delete allowed", null, "FakeHighRiskTool", """{"action":"delete"}""", "admin-1");
        var matching = AgentPolicy.Create("Read-only allowed", null, "FakeHighRiskTool", """{"action":"read-only"}""", "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([nonMatching, matching]);

        var evaluator = new AgentPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync("FakeHighRiskTool", """{"action":"read-only"}""", CancellationToken.None);

        match.Should().Be(matching);
    }
}
