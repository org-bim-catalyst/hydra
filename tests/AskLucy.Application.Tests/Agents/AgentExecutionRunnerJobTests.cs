using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md User Story 4 (FR-017, SC-009: ≤5s) — the orchestrator's step-boundary loop observes an
/// externally-requested pause/cancel (<c>PauseAgentExecutionCommand</c>/<c>CancelAgentExecutionCommand</c>,
/// each running in a separate HTTP request against its own tracked <c>AgentExecution</c>) and exits
/// immediately, never touching (or re-saving) this run's own now-stale in-memory execution.
///
/// Placed here rather than <c>tests/AskLucy.Infrastructure.Tests/</c> (as tasks.md's T119 literally
/// names it) because <see cref="AgentExecutionOrchestrator"/>/<see cref="AgentExecutionRunner"/> are
/// both Application-layer classes (research.md Decision 8, mirroring <c>IDocumentProcessingPipeline</c>'s
/// precedent) — the same correction already applied earlier in this feature's implementation.
/// </summary>
public sealed class AgentExecutionRunnerJobTests
{
    private const string OwnerId = "user-1";
    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAgentPlanner _planner = Substitute.For<IAgentPlanner>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();

    private (AgentExecution Execution, AgentVersion Version) CreateExecution()
    {
        var instructions = new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null);
        var agent = Agent.Create(OwnerId, "Two-Step Agent", null, AgentType.Task, instructions, Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Do two things.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        _executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<AgentToolCall>());
        _providerResolver.Resolve(Arg.Any<string>()).Returns(_aiProvider);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        _planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [
                new AgentPlanStep(0, "First step.", AgentExecutionStepType.ModelReasoning, null),
                new AgentPlanStep(1, "Second step.", AgentExecutionStepType.ModelReasoning, null),
            ]));

        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Some output.", new ChatUsage(10, 5, null, null, 50)));

        return (execution, version);
    }

    private AgentExecutionOrchestrator CreateOrchestrator() => new(
        _executionRepository, _agentRepository, _providerRepository, _modelRepository, _providerResolver, _planner,
        new AgentToolCatalog([]), new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
        new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
        Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), _chatRepository, _messageRepository, _unitOfWork);

    [Fact]
    public async Task RunAsync_ShouldExitAtTheNextStepBoundary_WhenAConcurrentPauseIsObserved()
    {
        var (execution, _) = CreateExecution();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>())
            .Returns(AgentExecutionStatus.Running, AgentExecutionStatus.Paused);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Steps.Should().ContainSingle();
        execution.Status.Should().Be(AgentExecutionStatus.Running);
    }

    [Fact]
    public async Task RunAsync_ShouldExitAtTheNextStepBoundary_WhenAConcurrentCancelIsObserved()
    {
        var (execution, _) = CreateExecution();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>())
            .Returns(AgentExecutionStatus.Running, AgentExecutionStatus.Cancelled);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Steps.Should().ContainSingle();
        execution.Status.Should().Be(AgentExecutionStatus.Running);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteNormally_WhenNoExternalPauseOrCancelIsObserved()
    {
        var (execution, _) = CreateExecution();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(AgentExecutionStatus.Running);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Steps.Should().HaveCount(2);
        execution.Status.Should().Be(AgentExecutionStatus.Completed);
    }
}
