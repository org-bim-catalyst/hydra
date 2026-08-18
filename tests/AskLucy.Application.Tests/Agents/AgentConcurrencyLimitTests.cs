using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.StartAgentExecution;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>spec.md FR-042/FR-043 — a user already at their concurrency cap is rejected with an actionable, 429-shaped error rather than silently exceeding the cap or queuing indefinitely.</summary>
public sealed class AgentConcurrencyLimitTests
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

    private (Agent Agent, AgentVersion Version) CreatePublishedAgent()
    {
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentRepository.GetVersionAsync(agent.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        return (agent, version);
    }

    private StartAgentExecutionCommandHandler CreateHandler(int defaultMaxConcurrentExecutions = 3) =>
        new(_agentRepository, _executionRepository, _auditLogRepository, _policyRepository, _chatRepository, _runner, _sender,
            Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions { DefaultMaxConcurrentExecutions = defaultMaxConcurrentExecutions }),
            _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldReject_WhenTheUserIsAlreadyAtTheSystemDefaultCap()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, _) = CreatePublishedAgent();
        _policyRepository.GetUserExecutionLimitAsync(OwnerId, Arg.Any<CancellationToken>()).Returns((AgentUserExecutionLimit?)null);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(3);

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.Standalone, null, false);
        var act = () => CreateHandler(defaultMaxConcurrentExecutions: 3).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<AgentConcurrencyLimitExceededException>();
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _executionRepository.DidNotReceive().Add(Arg.Any<AgentExecution>());
    }

    [Fact]
    public async Task Handle_ShouldAllow_WhenTheUserIsBelowTheSystemDefaultCap()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, _) = CreatePublishedAgent();
        _policyRepository.GetUserExecutionLimitAsync(OwnerId, Arg.Any<CancellationToken>()).Returns((AgentUserExecutionLimit?)null);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(2);

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.Standalone, null, false);
        await CreateHandler(defaultMaxConcurrentExecutions: 3).Handle(command, CancellationToken.None);

        await _runner.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseThePerUserOverride_InsteadOfTheSystemDefault_WhenOneIsSet()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (agent, _) = CreatePublishedAgent();
        var override_ = AgentUserExecutionLimit.Create(OwnerId, 1, "admin-1");
        _policyRepository.GetUserExecutionLimitAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(override_);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(1);

        var command = new StartAgentExecutionCommand(agent.Id, null, "Do the thing.", AgentConversationIntegrationMode.Standalone, null, false);
        var act = () => CreateHandler(defaultMaxConcurrentExecutions: 10).Handle(command, CancellationToken.None);

        // The system default (10) would allow this, but the per-user override (1) does not.
        await act.Should().ThrowAsync<AgentConcurrencyLimitExceededException>();
    }
}
