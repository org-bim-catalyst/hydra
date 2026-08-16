using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Workflows;

public sealed class WorkflowExecutionTests
{
    private const string UserId = "user-1";

    private static WorkflowExecution CreateExecution() =>
        WorkflowExecution.Create(Guid.NewGuid(), Guid.NewGuid(), UserId, WorkflowExecutionTriggerType.Manual, null, "{}", UserId);

    [Fact]
    public void Create_ShouldStartInQueuedStatus()
    {
        var execution = CreateExecution();

        execution.Status.Should().Be(WorkflowExecutionStatus.Queued);
        execution.StartedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldThrow_WhenRunByUserIdIsBlank()
    {
        var act = () => WorkflowExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "  ", WorkflowExecutionTriggerType.Manual, null, "{}", UserId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Start_ShouldTransitionToRunning_AndStampStartedAtUtcOnce()
    {
        var execution = CreateExecution();

        execution.Start();
        var firstStartedAt = execution.StartedAtUtc;
        execution.Start();

        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
        execution.StartedAtUtc.Should().Be(firstStartedAt);
    }

    [Fact]
    public void Pause_ThenResume_ShouldRoundTripThroughRunning()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Pause();
        execution.Status.Should().Be(WorkflowExecutionStatus.Paused);

        execution.Resume();
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public void AddNode_ShouldAppendToNodes()
    {
        var execution = CreateExecution();

        var node = execution.AddNode(Guid.NewGuid());

        execution.Nodes.Should().ContainSingle().Which.Should().BeSameAs(node);
    }

    [Fact]
    public void RequestApproval_ShouldTransitionToWaitingForApproval()
    {
        var execution = CreateExecution();
        execution.Start();
        var node = execution.AddNode(Guid.NewGuid());

        var approval = execution.RequestApproval(node.Id, "Send an email", "{}", timeoutSeconds: null);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        execution.Approvals.Should().ContainSingle().Which.Should().BeSameAs(approval);
    }

    [Fact]
    public void Resume_FromWaitingForApproval_ShouldReturnToRunning()
    {
        var execution = CreateExecution();
        execution.Start();
        var node = execution.AddNode(Guid.NewGuid());
        execution.RequestApproval(node.Id, "Send an email", "{}", null);

        execution.Resume();

        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public void Cancel_ShouldBeTerminal_AndRecordTheReason()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Cancel("User requested cancellation.");

        execution.Status.Should().Be(WorkflowExecutionStatus.Cancelled);
        execution.TerminationReason.Should().Be("User requested cancellation.");
        execution.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Complete_ShouldBeTerminal_AndStoreTheFinalOutput()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Complete("{\"result\":\"done\"}");

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.FinalOutputJson.Should().Be("{\"result\":\"done\"}");
    }

    [Fact]
    public void Fail_ShouldBeTerminal_AndRecordTheReason()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Fail("Budget exceeded.");

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.TerminationReason.Should().Be("Budget exceeded.");
    }

    [Fact]
    public void TimeOut_ShouldBeTerminal_AndRecordTheReason()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.TimeOut("Maximum execution duration exceeded.");

        execution.Status.Should().Be(WorkflowExecutionStatus.TimedOut);
        execution.TerminationReason.Should().Be("Maximum execution duration exceeded.");
    }

    [Fact]
    public void RecordError_ShouldAppendToErrors_WithoutChangingStatus()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.RecordError(WorkflowErrorCategory.NodeExecutionFailure, "The node failed.", workflowExecutionNodeId: null, retryCount: 1);

        execution.Errors.Should().ContainSingle();
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public void RecordEvent_ShouldAppendToEvents()
    {
        var execution = CreateExecution();

        var evt = execution.RecordEvent(WorkflowExecutionEventType.WorkflowStarted, workflowNodeId: null, "Running", null);

        execution.Events.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }
}
