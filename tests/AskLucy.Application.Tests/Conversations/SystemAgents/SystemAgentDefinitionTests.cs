using AskLucy.Application.Conversations.SystemAgents;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.SystemAgents;

/// <summary>
/// specs/045 T101, FR-035 — <see cref="SystemAgentDefinition.ComputeHash"/> is what gates a new
/// <c>AgentVersion</c> being published, so its only real contract is: identical definitions hash
/// identically, and a definition that actually differs never collides with one that doesn't.
/// </summary>
public sealed class SystemAgentDefinitionTests
{
    private static SystemAgentDefinition BuildDefinition(string description = "A test agent.") => new(
        "test.agent", "Test Agent", description, AgentType.Task, AiCapability.Chat,
        new AgentInstructions("Be helpful.", "Objective.", null, null, null, null, null),
        ["resolve_location", "adjust_viewer_focus"],
        AgentExecutionPolicy.Empty);

    [Fact]
    public void ComputeHash_ShouldBeStable_ForTwoIdenticalDefinitions()
    {
        BuildDefinition().ComputeHash().Should().Be(BuildDefinition().ComputeHash());
    }

    [Fact]
    public void ComputeHash_ShouldDiffer_WhenTheDescriptionChanges()
    {
        BuildDefinition("A test agent.").ComputeHash().Should().NotBe(BuildDefinition("A different description.").ComputeHash());
    }

    [Fact]
    public void ComputeHash_ShouldDiffer_WhenTheCapabilityKeysChange()
    {
        var original = BuildDefinition();
        var reordered = original with { CapabilityKeys = ["adjust_viewer_focus", "resolve_location"] };

        original.ComputeHash().Should().NotBe(reordered.ComputeHash());
    }

    [Fact]
    public void ComputeHash_ShouldDiffer_WhenAnExecutionPolicyFieldChanges()
    {
        var original = BuildDefinition();
        var withLimit = original with { ExecutionPolicy = original.ExecutionPolicy with { MaxSteps = 10 } };

        original.ComputeHash().Should().NotBe(withLimit.ComputeHash());
    }

    [Fact]
    public void AllDefinitions_ShouldHaveUniqueSystemKeysAndHashes()
    {
        SystemAgentDefinitions.All.Select(d => d.SystemKey).Should().OnlyHaveUniqueItems();
        SystemAgentDefinitions.All.Select(d => d.ComputeHash()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void OrchestratorDefinition_ShouldHoldNoCapabilityKeys()
    {
        // contracts/system-agent-provisioning.md §1 — the orchestrator plans and narrates; every
        // action goes through a sub-agent, never itself.
        SystemAgentDefinitions.All.Single(d => d.SystemKey == "lucy.orchestrator").CapabilityKeys.Should().BeEmpty();
    }
}
