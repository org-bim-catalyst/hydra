using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// T065c — invokes <see cref="AgentExecutionOrchestrator.RunAsync"/> in-process (research.md
/// Decision 3), never re-implementing agent planning. Uses a real, fully-wired
/// <see cref="AgentExecutionOrchestrator"/> (same construction as
/// <c>AgentExecutionOrchestratorTests</c>) rather than mocking it, since the whole point is to
/// prove the nested invocation actually runs the agent, not just that a method was called.
/// </summary>
public sealed class AgentNodeExecutorTests
{
    private const string OwnerId = "user-1";

    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAgentExecutionRepository _agentExecutionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentPolicyRepository _agentPolicyRepository = Substitute.For<IAgentPolicyRepository>();
    private readonly IAgentAuditLogRepository _agentAuditLogRepository = Substitute.For<IAgentAuditLogRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAgentPlanner _planner = Substitute.For<IAgentPlanner>();
    private readonly IAgentExecutionNotifier _notifier = Substitute.For<IAgentExecutionNotifier>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();

    private AgentExecution? _createdExecution;

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private AgentNodeExecutor CreateExecutor(int defaultMaxConcurrentExecutions = 3)
    {
        var toolCatalog = new AgentToolCatalog([], EmptyMcpToolRegistry());
        var budgetGuard = new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions()));
        var duplicateDetector = new AgentDuplicateToolCallDetector();
        var policyEvaluator = new AgentPolicyEvaluator(_agentPolicyRepository);

        var orchestrator = new AgentExecutionOrchestrator(
            _agentExecutionRepository, _agentRepository, _providerRepository, _modelRepository,
            _providerResolver, _planner, toolCatalog, budgetGuard, duplicateDetector, policyEvaluator, _notifier,
            _agentAuditLogRepository, _chatRepository, _messageRepository, _unitOfWork);

        _agentExecutionRepository.When(r => r.Add(Arg.Any<AgentExecution>())).Do(call => _createdExecution = call.Arg<AgentExecution>());
        _agentExecutionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _createdExecution);

        return new AgentNodeExecutor(
            _agentRepository, _agentExecutionRepository, _agentPolicyRepository, _agentAuditLogRepository, orchestrator,
            Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions { DefaultMaxConcurrentExecutions = defaultMaxConcurrentExecutions }),
            _expressionEvaluator, _unitOfWork);
    }

    private static readonly AIModelCapabilities NoCapabilities = new(
        Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false, Reasoning: false,
        Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false);

    private (Agent Agent, AgentVersion Version) SetUpPublishedAgent()
    {
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentRepository.GetVersionAsync(agent.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        _agentExecutionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgentToolCall>());
        _agentExecutionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        return (agent, version);
    }

    private static WorkflowNode BuildNode(string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "node", WorkflowNodeType.AiAgent, "node", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    [Fact]
    public async Task ExecuteAsync_ShouldRunTheAgentInProcess_AndReturnItsFinalOutput()
    {
        var (agent, _) = SetUpPublishedAgent();

        _planner.CreatePlanAsync(
                Arg.Any<string>(), Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new AgentPlan((string)callInfo[0], [new AgentPlanStep(0, "Respond.", AgentExecutionStepType.ModelReasoning, null)]));

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Nested agent result.", new ChatUsage(10, 5, null, null, 100)));
        _providerResolver.Resolve(Arg.Any<string>()).Returns(aiProvider);

        var configJson = JsonSerializer.Serialize(new { agentId = agent.Id, objective = "{{workflow.objective}}" });
        var node = BuildNode(configJson);
        using var input = JsonDocument.Parse("""{"workflow.objective":"Summarize the report."}""");

        var result = await CreateExecutor().ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("text").GetString().Should().Be("Nested agent result.");
        _agentExecutionRepository.Received(1).Add(Arg.Is<AgentExecution>(e => e.Objective == "Summarize the report." && e.RunByUserId == OwnerId));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenAtTheConcurrencyLimit()
    {
        var (agent, _) = SetUpPublishedAgent();
        _agentExecutionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(1);

        var configJson = JsonSerializer.Serialize(new { agentId = agent.Id, objective = "Say hello." });
        var node = BuildNode(configJson);
        using var input = JsonDocument.Parse("{}");

        var result = await CreateExecutor(defaultMaxConcurrentExecutions: 1).ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("limit");
        _agentExecutionRepository.DidNotReceive().Add(Arg.Any<AgentExecution>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheAgentHasNoPublishedVersion()
    {
        var agent = Agent.Create(
            OwnerId, "Draft Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        _agentRepository.GetByIdForOwnerAsync(agent.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        _agentExecutionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        var configJson = JsonSerializer.Serialize(new { agentId = agent.Id, objective = "Say hello." });
        var node = BuildNode(configJson);
        using var input = JsonDocument.Parse("{}");

        var result = await CreateExecutor().ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("published version");
    }
}
