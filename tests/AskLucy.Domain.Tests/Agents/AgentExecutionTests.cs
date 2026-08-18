using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Agents;

public sealed class AgentExecutionTests
{
    private const string UserId = "user-1";

    private static AgentExecution CreateExecution(AgentConversationIntegrationMode mode = AgentConversationIntegrationMode.Standalone, Guid? userChatId = null) =>
        AgentExecution.Create(Guid.NewGuid(), Guid.NewGuid(), UserId, "Do the thing.", isTestExecution: false, mode, userChatId, UserId);

    [Fact]
    public void Create_ShouldStartInQueuedStatus()
    {
        var execution = CreateExecution();

        execution.Status.Should().Be(AgentExecutionStatus.Queued);
        execution.StartedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldThrow_WhenObjectiveIsBlank()
    {
        var act = () => AgentExecution.Create(Guid.NewGuid(), Guid.NewGuid(), UserId, "  ", false, AgentConversationIntegrationMode.Standalone, null, UserId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldThrow_WhenExistingConversationModeHasNoUserChatId()
    {
        var act = () => AgentExecution.Create(Guid.NewGuid(), Guid.NewGuid(), UserId, "Do the thing.", false, AgentConversationIntegrationMode.ExistingConversation, null, UserId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Start_ShouldTransitionToRunning_AndStampStartedAtUtcOnce()
    {
        var execution = CreateExecution();

        execution.Start();
        var firstStartedAt = execution.StartedAtUtc;
        execution.Start();

        execution.Status.Should().Be(AgentExecutionStatus.Running);
        execution.StartedAtUtc.Should().Be(firstStartedAt);
    }

    [Fact]
    public void Pause_ThenResume_ShouldRoundTripThroughRunning()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Pause();
        execution.Status.Should().Be(AgentExecutionStatus.Paused);

        execution.Resume();
        execution.Status.Should().Be(AgentExecutionStatus.Running);
    }

    [Fact]
    public void RequestApproval_ShouldTransitionToWaitingForApproval()
    {
        var execution = CreateExecution();
        execution.Start();

        var approval = execution.RequestApproval(agentToolCallId: null, "Send an email", "{}");

        execution.Status.Should().Be(AgentExecutionStatus.WaitingForApproval);
        execution.Approvals.Should().ContainSingle().Which.Should().BeSameAs(approval);
    }

    [Fact]
    public void Resume_FromWaitingForApproval_ShouldReturnToRunning()
    {
        var execution = CreateExecution();
        execution.Start();
        execution.RequestApproval(null, "Send an email", "{}");

        execution.Resume();

        execution.Status.Should().Be(AgentExecutionStatus.Running);
    }

    [Fact]
    public void Cancel_ShouldBeTerminal_AndRecordTheReason()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Cancel("User requested cancellation.");

        execution.Status.Should().Be(AgentExecutionStatus.Cancelled);
        execution.TerminationReason.Should().Be("User requested cancellation.");
        execution.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Complete_ShouldBeTerminal_AndStoreTheFinalOutput()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Complete("Done.", finalOutputJson: null);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.FinalOutputText.Should().Be("Done.");
    }

    [Fact]
    public void Fail_ShouldBeTerminal_AndRecordTheReason()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.Fail("Budget exceeded.");

        execution.Status.Should().Be(AgentExecutionStatus.Failed);
        execution.TerminationReason.Should().Be("Budget exceeded.");
    }

    [Fact]
    public void RecordError_ShouldAppendToErrors_WithoutChangingStatus()
    {
        var execution = CreateExecution();
        execution.Start();

        execution.RecordError(AgentExecutionErrorCategory.ToolFailure, "The tool failed.", stepId: null, retryCount: 1);

        execution.Errors.Should().ContainSingle();
        execution.Status.Should().Be(AgentExecutionStatus.Running);
    }
}
