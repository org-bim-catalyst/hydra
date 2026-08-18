using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.PublishAgentVersion;
using AskLucy.Application.Agents.Queries.GetAgentVersion;
using AskLucy.Application.Agents.Queries.ListAgentVersions;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md User Story 6: publish v1, edit, publish v2; an execution started under v1 still
/// reports v1 after v2 exists — published versions are immutable snapshots, never mutated by a
/// later draft edit or publish.
/// </summary>
public sealed class AgentVersioningTests
{
    private const string OwnerId = "user-1";

    private static Agent CreateDraftAgent() => Agent.Create(
        OwnerId, "Versioned Agent", null, AgentType.Task,
        new AgentInstructions("v1 instructions.", null, null, null, null, null, null),
        Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

    [Fact]
    public async Task PublishAgentVersionCommandHandler_ShouldNeverMutateAnEarlierPublishedVersion_WhenTheDraftIsEditedAndRepublished()
    {
        var agent = CreateDraftAgent();
        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new PublishAgentVersionCommandHandler(agentRepository, unitOfWork, currentUser);

        var v1 = await handler.Handle(new PublishAgentVersionCommand(agent.Id, "First release."), CancellationToken.None);

        agent.UpdateDraft(
            "Versioned Agent", null, AgentType.Task,
            new AgentInstructions("v2 instructions — completely different.", null, null, null, null, null, null),
            agent.ModelProviderId, agent.ModelId, agent.OutputFormat, agent.ExecutionPolicy, OwnerId);

        var v2 = await handler.Handle(new PublishAgentVersionCommand(agent.Id, "Second release."), CancellationToken.None);

        v1.VersionNumber.Should().Be(1);
        v2.VersionNumber.Should().Be(2);
        v1.Instructions.SystemInstructions.Should().Be("v1 instructions.");
        v2.Instructions.SystemInstructions.Should().Be("v2 instructions — completely different.");
        agent.PublishedVersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task AnExecution_ShouldStillReferenceItsOriginalVersion_AfterANewerVersionIsPublished()
    {
        var agent = CreateDraftAgent();
        var v1 = agent.Publish("First release.", OwnerId);

        var execution = AgentExecution.Create(agent.Id, v1.Id, OwnerId, "Do the v1 thing.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        agent.UpdateDraft(
            "Versioned Agent", null, AgentType.Task,
            new AgentInstructions("v2 instructions.", null, null, null, null, null, null),
            agent.ModelProviderId, agent.ModelId, agent.OutputFormat, agent.ExecutionPolicy, OwnerId);
        var v2 = agent.Publish("Second release.", OwnerId);

        execution.AgentVersionId.Should().Be(v1.Id);
        execution.AgentVersionId.Should().NotBe(v2.Id);
        agent.PublishedVersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task ListAgentVersionsQueryHandler_ShouldReturnEveryPublishedVersion_NewestFirst()
    {
        var agent = CreateDraftAgent();
        var v1 = agent.Publish("First.", OwnerId);
        var v2 = agent.Publish("Second.", OwnerId);

        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        agentRepository.ListVersionsAsync(agent.Id, Arg.Any<CancellationToken>()).Returns([v1, v2]);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new ListAgentVersionsQueryHandler(agentRepository, currentUser);
        var result = await handler.Handle(new ListAgentVersionsQuery(agent.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].VersionNumber.Should().Be(2);
        result[1].VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetAgentVersionQueryHandler_ShouldThrowNotFound_WhenTheVersionNumberDoesNotExist()
    {
        var agent = CreateDraftAgent();
        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        agentRepository.GetVersionAsync(agent.Id, 99, Arg.Any<CancellationToken>()).Returns((AgentVersion?)null);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new GetAgentVersionQueryHandler(agentRepository, currentUser);
        var act = () => handler.Handle(new GetAgentVersionQuery(agent.Id, 99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
