using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.StartAgentExecution;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.CreateUserChat;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class StartAgentExecutionCommandHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentAuditLogRepository _auditLogRepository = Substitute.For<IAgentAuditLogRepository>();
    private readonly IAgentPolicyRepository _policyRepository = Substitute.For<IAgentPolicyRepository>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IAgentExecutionRunner _runner = Substitute.For<IAgentExecutionRunner>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private StartAgentExecutionCommandHandler CreateHandler() =>
        new(_agentRepository, _executionRepository, _auditLogRepository, _policyRepository, _chatRepository, _runner, _sender,
            Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions()), _unitOfWork, _currentUser);

    private static (Agent Agent, AgentVersion Version) CreatePublishedAgent()
    {
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);
        return (agent, version);
    }

    [Fact]
    public async Task Handle_ShouldCreateExecutionAndEnqueueTheRunner_ForStandaloneMode()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, version) = CreatePublishedAgent();
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentRepository.GetVersionAsync(agent.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.Standalone, null, false);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.AgentId.Should().Be(agent.Id);
        _executionRepository.Received(1).Add(Arg.Is<AgentExecution>(e => e != null && e.UserChatId == null && e.ConversationIntegrationMode == AgentConversationIntegrationMode.Standalone));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _runner.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenAgentHasNoPublishedVersionAndNoneSpecified()
    {
        _currentUser.UserId.Returns(OwnerId);
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.Standalone, null, false);
        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCreateANewConversation_WhenModeIsNewConversation()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, version) = CreatePublishedAgent();
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentRepository.GetVersionAsync(agent.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        var newChatId = Guid.NewGuid();
        _sender.Send(Arg.Any<CreateUserChatCommand>(), Arg.Any<CancellationToken>())
            .Returns(new UserChatDto(newChatId, "Agent: My Agent", DateTime.UtcNow, null));

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.NewConversation, null, false);
        await CreateHandler().Handle(command, CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Is<AgentExecution>(e => e != null && e.UserChatId == newChatId));
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenExistingConversationIsNotOwnedByTheCaller()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, version) = CreatePublishedAgent();
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentRepository.GetVersionAsync(agent.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        var someoneElsesChat = UserChat.Create("Not mine", "user-2", null, "user-2");
        _chatRepository.GetByIdAsync(someoneElsesChat.Id, Arg.Any<CancellationToken>()).Returns(someoneElsesChat);

        var command = new StartAgentExecutionCommand(
            agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.ExistingConversation, someoneElsesChat.Id, false);
        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
