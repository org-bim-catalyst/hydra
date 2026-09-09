using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 T041/T111, FR-038 — <see cref="TurnRecorder"/> writes one <see cref="AgentExecution"/>
/// per capability-invoking turn, attributed to the provisioned <c>lucy.orchestrator</c> agent.
/// </summary>
public sealed class TurnRecorderTests
{
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Guid _chatId = Guid.NewGuid();

    private TurnRecorder BuildRecorder() =>
        new(_agentRepository, _executionRepository, _unitOfWork, NullLogger<TurnRecorder>.Instance);

    private Agent SeedProvisionedOrchestrator()
    {
        var agent = Agent.CreateSystemProvisioned(
            TurnRecorder.OrchestratorSystemKey, "Lucy", null, AgentType.Conversational,
            AgentInstructions.Empty, AiCapability.TurnOrchestration, AgentExecutionPolicy.Empty, "system:test");
        agent.PublishSystemVersion([], "test-hash", "system:test");
        _agentRepository.GetBySystemKeyAsync(TurnRecorder.OrchestratorSystemKey, Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    /// <summary>
    /// Registers the capture callback before <c>RecordAsync</c> runs, but hands back a box read
    /// AFTER the act — the execution doesn't exist yet at the moment this method itself returns.
    /// </summary>
    private sealed class ExecutionBox
    {
        public AgentExecution? Value { get; set; }
    }

    private ExecutionBox TrackAddedExecution()
    {
        var box = new ExecutionBox();
        _executionRepository.When(r => r.Add(Arg.Any<AgentExecution>())).Do(call => box.Value = call.Arg<AgentExecution>());
        return box;
    }

    [Fact]
    public async Task RecordAsync_ShouldWriteNothing_WhenTheUserIsUnauthenticated()
    {
        SeedProvisionedOrchestrator();

        await BuildRecorder().RecordAsync(_chatId, userId: null, "do something", "{}", [], "done", CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<AgentExecution>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_ShouldWriteNothing_WhenTheOrchestratorIsNotYetProvisioned()
    {
        _agentRepository.GetBySystemKeyAsync(TurnRecorder.OrchestratorSystemKey, Arg.Any<CancellationToken>()).Returns((Agent?)null);

        await BuildRecorder().RecordAsync(_chatId, "user-1", "do something", "{}", [], "done", CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<AgentExecution>());
    }

    [Fact]
    public async Task RecordAsync_ShouldPersistTheExecution_AttributedToTheOrchestratorsRealAgentAndVersion()
    {
        var orchestrator = SeedProvisionedOrchestrator();
        var recordedBox = TrackAddedExecution();

        await BuildRecorder().RecordAsync(
            _chatId, "user-1", "show me the park", """{"intent":"act"}""",
            [new TurnRecordedStep("resolve_location", Attempted: true, Succeeded: true, """{"locationName":"Al Safa Park 2"}""", Reason: null)],
            "Found it.", CancellationToken.None);
        var recorded = recordedBox.Value;

        recorded.Should().NotBeNull();
        recorded!.AgentId.Should().Be(orchestrator.Id);
        recorded.AgentVersionId.Should().Be(orchestrator.Versions.Single().Id);
        recorded.UserChatId.Should().Be(_chatId);
        recorded.RunByUserId.Should().Be("user-1");
        recorded.Status.Should().Be(AgentExecutionStatus.Completed);
        recorded.PlanJson.Should().Be("""{"intent":"act"}""");
        recorded.Steps.Should().ContainSingle();
        recorded.Steps.Single().Status.Should().Be(AgentExecutionStepStatus.Completed);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_ShouldRecordAFailedStep_WithAnAttachedError()
    {
        SeedProvisionedOrchestrator();
        var recordedBox = TrackAddedExecution();

        await BuildRecorder().RecordAsync(
            _chatId, "user-1", "outline the site", "{}",
            [new TurnRecordedStep("resolve_site_boundary", Attempted: true, Succeeded: false, ResultJson: null, "the boundary lookup failed")],
            "It didn't work.", CancellationToken.None);
        var recorded = recordedBox.Value;

        var step = recorded!.Steps.Single();
        step.Status.Should().Be(AgentExecutionStepStatus.Failed);
        step.ErrorId.Should().NotBeNull();
        recorded.Errors.Should().ContainSingle(e => e.Message == "the boundary lookup failed");
    }

    [Fact]
    public async Task RecordAsync_ShouldRecordAnUnattemptedStep_AsSkipped_WithItsReason()
    {
        SeedProvisionedOrchestrator();
        var recordedBox = TrackAddedExecution();

        await BuildRecorder().RecordAsync(
            _chatId, "user-1", "find and outline the site", "{}",
            [new TurnRecordedStep("resolve_site_boundary", Attempted: false, Succeeded: false, ResultJson: null, "not attempted — an earlier required step failed")],
            "I stopped there.", CancellationToken.None);
        var recorded = recordedBox.Value;

        var step = recorded!.Steps.Single();
        step.Status.Should().Be(AgentExecutionStepStatus.Skipped);
        step.OutputJson.Should().Be("not attempted — an earlier required step failed");
    }

    [Fact]
    public async Task RecordAsync_ShouldAssignStrictlyIncreasingUniqueStepIndices()
    {
        SeedProvisionedOrchestrator();
        var recordedBox = TrackAddedExecution();

        await BuildRecorder().RecordAsync(
            _chatId, "user-1", "do two things", "{}",
            [
                new TurnRecordedStep("resolve_location", true, true, "{}", null),
                new TurnRecordedStep("search_knowledge_base", true, true, "{}", null),
            ],
            "Done.", CancellationToken.None);
        var recorded = recordedBox.Value;

        recorded!.Steps.Select(s => s.StepIndex).Should().Equal(0, 1);
    }

    [Fact]
    public async Task RecordAsync_ShouldNotThrow_WhenPersistenceFails()
    {
        SeedProvisionedOrchestrator();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("db unavailable"));

        var act = async () => await BuildRecorder().RecordAsync(_chatId, "user-1", "do something", "{}", [], "done", CancellationToken.None);

        // constitution §2.VIII — isolation, not suppression: the turn's own user-visible outcome
        // was already decided and streamed by the time this runs, so a recording failure must
        // never propagate back into it. Logged instead (TurnRecorderLog.RecordingFailed).
        await act.Should().NotThrowAsync();
    }
}
