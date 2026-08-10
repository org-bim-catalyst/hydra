using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.PublishAgentVersion;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class PublishAgentVersionCommandHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private PublishAgentVersionCommandHandler CreateHandler() => new(_agentRepository, _unitOfWork, _currentUser);

    private static Agent CreateAgent() => Agent.Create(
        OwnerId, "My Agent", null, AgentType.Task,
        new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
        Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

    [Fact]
    public async Task Handle_ShouldPublishAnImmutableSnapshot_AndReturnVersionOne()
    {
        _currentUser.UserId.Returns(OwnerId);
        var agent = CreateAgent();
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);

        var result = await CreateHandler().Handle(new PublishAgentVersionCommand(agent.Id, "Initial publish"), CancellationToken.None);

        result.VersionNumber.Should().Be(1);
        result.ChangeDescription.Should().Be("Initial publish");
        agent.Status.Should().Be(AgentStatus.Published);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenAgentIsNotOwnedByTheCaller()
    {
        _currentUser.UserId.Returns(OwnerId);
        _agentRepository.GetByIdForOwnerAsync(Arg.Any<Guid>(), OwnerId, Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var act = () => CreateHandler().Handle(new PublishAgentVersionCommand(Guid.NewGuid(), null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenAgentHasNoModelSelected()
    {
        _currentUser.UserId.Returns(OwnerId);
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            null, null, AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);

        var act = () => CreateHandler().Handle(new PublishAgentVersionCommand(agent.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
