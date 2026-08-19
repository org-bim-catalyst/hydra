using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md FR-031 — an agent execution calling an `Active` MCP tool end-to-end through the
/// unmodified `AgentExecutionOrchestrator` (no MCP-specific branch anywhere in the orchestrator;
/// contracts/mcp-tool-adapter.md's "Agent Runtime remains MCP-agnostic"), producing an
/// `AgentToolCall` row shaped identically to a native tool's.
/// </summary>
public sealed class McpToolExecutionOrchestratorIntegrationTests
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
    private readonly IMcpClient _mcpClient = Substitute.For<IMcpClient>();

    private static readonly AIModelCapabilities NoCapabilities = new(
        Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false, Reasoning: false,
        Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false);

    private McpTool CreateActiveMcpTool()
    {
        var tool = McpTool.CreateFromDiscovery(
            Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, AgentToolRiskLevel.Low, "[]", null, null);
        tool.Activate(OwnerId, null, null);
        return tool;
    }

    private AgentToolCatalog CreateToolCatalogWith(McpTool mcpTool)
    {
        var clientFactory = Substitute.For<IMcpClientFactory>();
        clientFactory.GetOrCreateAsync(mcpTool.McpServerId, Arg.Any<CancellationToken>()).Returns(_mcpClient);
        var rateLimiter = Substitute.For<IMcpRateLimiter>();
        rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        var resiliencePolicy = new McpConnectionResiliencePolicy(
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>());
        var adapter = new McpToolAdapter(
            mcpTool, "Test Server", clientFactory, rateLimiter, new JsonSchemaValidator(), resiliencePolicy,
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));

        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[adapter]);
        return new AgentToolCatalog([], registry);
    }

    private AgentExecutionOrchestrator CreateOrchestrator(AgentToolCatalog toolCatalog) => new(
        _executionRepository, _agentRepository, _providerRepository, _modelRepository,
        _providerResolver, _planner, toolCatalog, new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
        new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(_policyRepository), _notifier,
        Substitute.For<IAgentAuditLogRepository>(), _chatRepository, _messageRepository, _unitOfWork);

    private (AgentExecution Execution, McpTool McpTool) SetUpExecutionWithMcpTool()
    {
        var mcpTool = CreateActiveMcpTool();

        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool(mcpTool.NamespacedName, null, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(
            agent.Id, version.Id, OwnerId, "Search for something.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        _executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgentToolCall>());

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("{}", new ChatUsage(10, 5, null, null, 100)));
        _providerResolver.Resolve(Arg.Any<string>()).Returns(aiProvider);

        _planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Search.", AgentExecutionStepType.ToolCall, mcpTool.NamespacedName)]));

        return (execution, mcpTool);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteSuccessfully_WhenCallingAnActiveMcpTool_IndistinguishableFromANativeToolCall()
    {
        var (execution, mcpTool) = SetUpExecutionWithMcpTool();
        _mcpClient.CallToolAsync(mcpTool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(false, JsonDocument.Parse("{}"), null));
        var orchestrator = CreateOrchestrator(CreateToolCatalogWith(mcpTool));

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.Steps.Should().ContainSingle(s => s.StepType == AgentExecutionStepType.ToolCall && s.Status == AgentExecutionStepStatus.Completed && s.ToolName == mcpTool.NamespacedName);
        _executionRepository.Received(1).AddToolCall(Arg.Is<AgentToolCall>(tc => tc.ToolName == mcpTool.NamespacedName));
    }

    [Fact]
    public async Task RunAsync_ShouldRecordAToolFailureError_WhenTheMcpServerReportsAnError()
    {
        var (execution, mcpTool) = SetUpExecutionWithMcpTool();
        _mcpClient.CallToolAsync(mcpTool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(true, null, "downstream failure"));
        var orchestrator = CreateOrchestrator(CreateToolCatalogWith(mcpTool));

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Errors.Should().ContainSingle(e => e.Category == AgentExecutionErrorCategory.ToolFailure);
        _executionRepository.Received(1).AddToolCall(Arg.Is<AgentToolCall>(tc => tc.FailureReason != null));
    }
}
