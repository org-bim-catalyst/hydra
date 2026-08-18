using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.ArchiveAgent;
using AskLucy.Application.Agents.Commands.DeleteAgent;
using AskLucy.Application.Agents.Commands.DuplicateAgent;
using AskLucy.Application.Agents.Commands.RestoreAgent;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class DuplicateArchiveRestoreAgentTests
{
    private const string OwnerId = "user-1";

    private static Agent CreatePublishedAgent()
    {
        var agent = Agent.Create(
            OwnerId, "Original Agent", "Does things.", AgentType.Task,
            new AgentInstructions("Be helpful.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool("KnowledgeSearchTool", null, OwnerId);
        agent.Publish(null, OwnerId);
        return agent;
    }

    [Fact]
    public async Task DuplicateAgentCommandHandler_ShouldCopyTheDraftOnly_AsANewDraftAgent()
    {
        var agent = CreatePublishedAgent();
        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new DuplicateAgentCommandHandler(agentRepository, unitOfWork, currentUser);
        var result = await handler.Handle(new DuplicateAgentCommand(agent.Id), CancellationToken.None);

        result.Id.Should().NotBe(agent.Id);
        result.Name.Should().Be("Original Agent (Copy)");
        result.Status.Should().Be(nameof(AgentStatus.Draft));
        result.PublishedVersionNumber.Should().BeNull();
        result.ToolNames.Should().Contain("KnowledgeSearchTool");
        agentRepository.Received(1).Add(Arg.Is<Agent>(a => a.Id != agent.Id));
    }

    [Fact]
    public async Task ArchiveThenRestoreAgentCommandHandlers_ShouldReturnToThePreArchiveStatus()
    {
        var agent = CreatePublishedAgent();
        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var archiveHandler = new ArchiveAgentCommandHandler(agentRepository, unitOfWork, currentUser);
        var archived = await archiveHandler.Handle(new ArchiveAgentCommand(agent.Id), CancellationToken.None);
        archived.Status.Should().Be(nameof(AgentStatus.Archived));

        var restoreHandler = new RestoreAgentCommandHandler(agentRepository, unitOfWork, currentUser);
        var restored = await restoreHandler.Handle(new RestoreAgentCommand(agent.Id), CancellationToken.None);
        restored.Status.Should().Be(nameof(AgentStatus.Published));
    }

    [Fact]
    public async Task DeleteAgentCommandHandler_ShouldSoftDeleteOnly_NeverTouchingVersionsOrExecutions()
    {
        var agent = CreatePublishedAgent();
        var agentRepository = Substitute.For<IAgentRepository>();
        agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new DeleteAgentCommandHandler(agentRepository, unitOfWork, currentUser);
        await handler.Handle(new DeleteAgentCommand(agent.Id), CancellationToken.None);

        agent.DeletedAtUtc.Should().NotBeNull();
        agent.DeletedBy.Should().Be(OwnerId);
        agent.PublishedVersionNumber.Should().Be(1);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
