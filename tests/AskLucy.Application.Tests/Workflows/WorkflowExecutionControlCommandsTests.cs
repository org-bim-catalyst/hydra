using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Commands.CancelWorkflowExecution;
using AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;
using AskLucy.Application.Workflows.Commands.ResumeWorkflowExecution;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>spec.md User Story 6 — pause/resume/cancel command handlers, mirroring <c>AgentExecutionControlCommandsTests</c>.</summary>
public sealed class WorkflowExecutionControlCommandsTests
{
    private const string OwnerId = "user-1";

    private static WorkflowExecution CreateRunningExecution()
    {
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var execution = WorkflowExecution.Create(workflowId, versionId, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        execution.Start();
        return execution;
    }

    [Fact]
    public async Task PauseWorkflowExecutionCommandHandler_ShouldPauseARunningExecution_AndNotify()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new PauseWorkflowExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        await handler.Handle(new PauseWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Paused);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await notifier.Received(1).NotifyWorkflowPausedAsync(OwnerId, execution.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseWorkflowExecutionCommandHandler_ShouldBeANoOp_WhenTheExecutionIsNotRunning()
    {
        var execution = CreateRunningExecution();
        execution.Complete(null);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new PauseWorkflowExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        await handler.Handle(new PauseWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyWorkflowPausedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeWorkflowExecutionCommandHandler_ShouldResumeAndReenqueue_WhenPaused()
    {
        var execution = CreateRunningExecution();
        execution.Pause();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new ResumeWorkflowExecutionCommandHandler(executionRepository, runner, notifier, unitOfWork, currentUser);
        await handler.Handle(new ResumeWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
        await notifier.Received(1).NotifyWorkflowResumedAsync(OwnerId, execution.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await runner.Received(1).EnqueueAsync(execution.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeWorkflowExecutionCommandHandler_ShouldThrow_WhenTheExecutionIsNotPaused()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new ResumeWorkflowExecutionCommandHandler(executionRepository, runner, notifier, unitOfWork, currentUser);
        var act = () => handler.Handle(new ResumeWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelWorkflowExecutionCommandHandler_ShouldCancelARunningExecution_AndNotify()
    {
        var execution = CreateRunningExecution();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new CancelWorkflowExecutionCommandHandler(executionRepository, notifier, Substitute.For<IWorkflowAuditLogRepository>(), unitOfWork, currentUser);
        await handler.Handle(new CancelWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Cancelled);
        await notifier.Received(1).NotifyWorkflowCancelledAsync(OwnerId, execution.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelWorkflowExecutionCommandHandler_ShouldBeANoOp_WhenTheExecutionIsAlreadyTerminal()
    {
        var execution = CreateRunningExecution();
        execution.Complete(null);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new CancelWorkflowExecutionCommandHandler(executionRepository, notifier, Substitute.For<IWorkflowAuditLogRepository>(), unitOfWork, currentUser);
        await handler.Handle(new CancelWorkflowExecutionCommand(execution.Id), CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyWorkflowCancelledAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
