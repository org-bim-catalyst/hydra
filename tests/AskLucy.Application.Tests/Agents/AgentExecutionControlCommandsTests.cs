using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.CancelAgentExecution;
using AskLucy.Application.Agents.Commands.PauseAgentExecution;
using AskLucy.Application.Agents.Commands.ResumeAgentExecution;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class AgentExecutionControlCommandsTests
{
    private const string OwnerId = "user-1";

    private static AgentExecution CreateRunningExecution()
    {
        var agentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var execution = AgentExecution.Create(agentId, versionId, OwnerId, "Do something.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);
        execution.Start();
        return execution;
    }

    [Fact]
    public async Task PauseAgentExecutionCommandHandler_ShouldPauseARunningExecution()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new PauseAgentExecutionCommandHandler(executionRepository, unitOfWork, currentUser);
        await handler.Handle(new PauseAgentExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Paused);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAgentExecutionCommandHandler_ShouldResumeAndReenqueue_WhenPaused()
    {
        var execution = CreateRunningExecution();
        execution.Pause();
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var runner = Substitute.For<IAgentExecutionRunner>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new ResumeAgentExecutionCommandHandler(executionRepository, runner, unitOfWork, currentUser);
        await handler.Handle(new ResumeAgentExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Running);
        await runner.Received(1).EnqueueAsync(execution.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAgentExecutionCommandHandler_ShouldThrow_WhenTheExecutionIsNotPaused()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var runner = Substitute.For<IAgentExecutionRunner>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new ResumeAgentExecutionCommandHandler(executionRepository, runner, unitOfWork, currentUser);
        var act = () => handler.Handle(new ResumeAgentExecutionCommand(execution.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAgentExecutionCommandHandler_ShouldCancelARunningExecution_AndNotify()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IAgentExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new CancelAgentExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        await handler.Handle(new CancelAgentExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Cancelled);
        await notifier.Received(1).NotifyExecutionCancelledAsync(OwnerId, execution.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAgentExecutionCommandHandler_ShouldBeANoOp_WhenTheExecutionIsAlreadyTerminal()
    {
        var execution = CreateRunningExecution();
        execution.Complete("Done.", null);
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IAgentExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new CancelAgentExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        await handler.Handle(new CancelAgentExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyExecutionCancelledAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
