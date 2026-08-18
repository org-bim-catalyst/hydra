using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Agents;

public sealed class AgentTests
{
    private const string OwnerId = "user-1";

    private static readonly Guid ProviderId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();

    private static AgentInstructions Instructions(string systemInstructions = "You are a helpful assistant.") =>
        new(systemInstructions, Objectives: null, Constraints: null, BehavioralRules: null, OutputRequirements: null, ToolUsageRules: null, SafetyRules: null);

    private static Agent CreateAgent(string name = "My Agent") => Agent.Create(
        OwnerId, name, "desc", AgentType.Task, Instructions(), ProviderId, ModelId,
        AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

    [Fact]
    public void Create_ShouldStartInDraftStatus()
    {
        var agent = CreateAgent();

        agent.Status.Should().Be(AgentStatus.Draft);
        agent.PublishedVersionNumber.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldThrow_WhenNameIsBlank()
    {
        var act = () => Agent.Create(OwnerId, "  ", null, AgentType.Task, Instructions(), ProviderId, ModelId, AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_ShouldCreateVersionOne_AndTransitionToPublished()
    {
        var agent = CreateAgent();

        var version = agent.Publish(changeDescription: null, OwnerId);

        version.VersionNumber.Should().Be(1);
        agent.Status.Should().Be(AgentStatus.Published);
        agent.PublishedVersionNumber.Should().Be(1);
        agent.Versions.Should().ContainSingle().Which.Should().BeSameAs(version);
    }

    [Fact]
    public void Publish_ShouldThrow_WhenNoModelSelected()
    {
        var agent = Agent.Create(OwnerId, "My Agent", null, AgentType.Task, Instructions(), null, null, AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

        var act = () => agent.Publish(null, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_Twice_ShouldProduceASecondImmutableVersion_LeavingTheFirstUnchanged()
    {
        var agent = CreateAgent();
        var version1 = agent.Publish(null, OwnerId);

        agent.UpdateDraft("My Agent", null, AgentType.Task, Instructions("Updated instructions."), ProviderId, ModelId, AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version2 = agent.Publish("Updated instructions", OwnerId);

        version2.VersionNumber.Should().Be(2);
        agent.PublishedVersionNumber.Should().Be(2);
        agent.Versions.Should().HaveCount(2);
        version1.Instructions.SystemInstructions.Should().Be("You are a helpful assistant.");
        version2.Instructions.SystemInstructions.Should().Be("Updated instructions.");
    }

    [Fact]
    public void Duplicate_ShouldCopyDraftOnly_NeverVersionOrExecutionHistory()
    {
        var agent = CreateAgent();
        agent.AddTool("KnowledgeSearchTool", null, OwnerId);
        agent.Publish(null, OwnerId);

        var copy = agent.Duplicate(OwnerId);

        copy.Id.Should().NotBe(agent.Id);
        copy.Status.Should().Be(AgentStatus.Draft);
        copy.Versions.Should().BeEmpty();
        copy.Tools.Should().ContainSingle(t => t.ToolName == "KnowledgeSearchTool");
    }

    [Fact]
    public void Archive_FromDraft_ThenRestore_ShouldReturnToDraft_NotPublished()
    {
        var agent = CreateAgent();

        agent.Archive(OwnerId);
        agent.Status.Should().Be(AgentStatus.Archived);

        agent.Restore(OwnerId);

        agent.Status.Should().Be(AgentStatus.Draft);
    }

    [Fact]
    public void Archive_FromPublished_ThenRestore_ShouldReturnToPublished()
    {
        var agent = CreateAgent();
        agent.Publish(null, OwnerId);

        agent.Archive(OwnerId);
        agent.Restore(OwnerId);

        agent.Status.Should().Be(AgentStatus.Published);
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit()
    {
        var agent = CreateAgent();

        agent.SoftDelete(OwnerId);

        agent.IsDeleted.Should().BeTrue();
        agent.DeletedBy.Should().Be(OwnerId);
    }
}
