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

public sealed class AgentExecutionOrchestratorTests
{
    private const string OwnerId = "user-1";

    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAgentPlanner _planner = Substitute.For<IAgentPlanner>();
    private readonly IAgentPolicyRepository _policyRepository = Substitute.For<IAgentPolicyRepository>();
    private readonly IAgentExecutionNotifier _notifier = Substitute.For<IAgentExecutionNotifier>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    // Sealed, dependency-free (or trivially-configured) helpers — real instances rather than
    // mocks, matching how the codebase already treats pure helpers like PromptVariableResolver.
    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private readonly AgentToolCatalog _toolCatalog = new([], EmptyMcpToolRegistry());
    private readonly AgentBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions()));
    private readonly AgentDuplicateToolCallDetector _duplicateDetector = new();
    private readonly AgentPolicyEvaluator _policyEvaluator;

    public AgentExecutionOrchestratorTests()
    {
        _policyEvaluator = new AgentPolicyEvaluator(_policyRepository);
    }

    private AgentExecutionOrchestrator CreateOrchestrator() => new(
        _executionRepository, _agentRepository, _providerRepository, _modelRepository,
        _providerResolver, _planner, _toolCatalog, _budgetGuard, _duplicateDetector, _policyEvaluator, _notifier,
        Substitute.For<IAgentAuditLogRepository>(), _chatRepository, _messageRepository, _unitOfWork);

    private static readonly AIModelCapabilities NoCapabilities = new(
        Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false, Reasoning: false,
        Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false);

    private (AgentExecution Execution, AgentVersion Version, AIProvider Provider, AIModel Model) SetUpExecution()
    {
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(
            agent.Id, version.Id, OwnerId, "Say hello.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        _executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgentToolCall>());

        return (execution, version, provider, model);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteWithTheModelsResponse_ForASingleModelReasoningStep()
    {
        var (execution, _, _, _) = SetUpExecution();

        _planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Respond.", AgentExecutionStepType.ModelReasoning, null)]));

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Hello there!", new ChatUsage(10, 5, null, null, 100)));
        _providerResolver.Resolve(Arg.Any<string>()).Returns(aiProvider);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.FinalOutputText.Should().Be("Hello there!");
        execution.Steps.Should().ContainSingle(s => s.Status == AgentExecutionStepStatus.Completed);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldRecordAnErrorAndFail_WhenThePlannerThrows()
    {
        var (execution, _, _, _) = SetUpExecution();

        _planner.CreatePlanAsync(
                Arg.Any<string>(), Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<AgentPlan>(_ => throw new InvalidOperationException("The model did not return a valid plan."));

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle();
        execution.TerminationReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldBeANoOp_WhenTheExecutionIsAlreadyTerminal()
    {
        var (execution, _, _, _) = SetUpExecution();
        execution.Start();
        execution.Cancel("Already cancelled.");

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        await _planner.DidNotReceive().CreatePlanAsync(
            Arg.Any<string>(), Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
