using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// research.md Decisions 3/6 — <c>AgentPolicy.ToolName</c>/<c>AgentPolicyEvaluator</c> are unmodified,
/// generic string-matching code (no MCP-specific branch); this proves a namespaced
/// `mcp:{serverId}:{toolName}` string round-trips through the exact same policy-matching path a
/// native tool's plain identifier does, with zero orchestrator code change.
/// </summary>
public sealed class McpNamespacedToolPolicyMatchingTests
{
    private readonly IAgentPolicyRepository _policyRepository = Substitute.For<IAgentPolicyRepository>();

    [Fact]
    public async Task FindMatchAsync_ShouldMatchAnUnconditionalPolicy_AgainstANamespacedMcpToolName()
    {
        var namespacedName = $"mcp:{Guid.NewGuid()}:search";
        var policy = AgentPolicy.Create("Allow MCP search", null, namespacedName, null, "admin-1");
        _policyRepository.ListEnabledByToolNameAsync(namespacedName, Arg.Any<CancellationToken>()).Returns([policy]);
        var evaluator = new AgentPolicyEvaluator(_policyRepository);

        var match = await evaluator.FindMatchAsync(namespacedName, "{}", CancellationToken.None);

        match.Should().Be(policy);
    }

    [Fact]
    public async Task FindMatchAsync_ShouldRespectConditions_AgainstANamespacedMcpToolName()
    {
        var namespacedName = $"mcp:{Guid.NewGuid()}:deleteRecord";
        var policy = AgentPolicy.Create("Allow read-only", null, namespacedName, """{"mode":"read-only"}""", "admin-1");
        _policyRepository.ListEnabledByToolNameAsync(namespacedName, Arg.Any<CancellationToken>()).Returns([policy]);
        var evaluator = new AgentPolicyEvaluator(_policyRepository);

        var mismatched = await evaluator.FindMatchAsync(namespacedName, """{"mode":"destructive"}""", CancellationToken.None);
        var matched = await evaluator.FindMatchAsync(namespacedName, """{"mode":"read-only"}""", CancellationToken.None);

        mismatched.Should().BeNull();
        matched.Should().Be(policy);
    }

    [Fact]
    public void AgentToolCall_ShouldAcceptANamespacedMcpToolName_WithoutAnyDomainRuleViolation()
    {
        var namespacedName = $"mcp:{Guid.NewGuid()}:search";

        var act = () => AgentToolCall.Create(Guid.NewGuid(), namespacedName, AgentToolRiskLevel.Critical, "[]", "{}", wasApprovalRequired: true);

        act.Should().NotThrow();
    }
}
