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
/// spec.md FR-041: when a conflicting write to a protected resource occurs mid-execution (e.g.
/// two of a user's executions racing to create the same memory candidate), the execution rejects
/// the conflicting action with an actionable error rather than silently overwriting or crashing
/// unrecorded. <c>AgentExecutionOrchestrator</c> deliberately catches <see cref="Exception"/>
/// generically at the top level — never the EF Core-specific <c>DbUpdateConcurrencyException</c>,
/// which <c>Application</c> must not reference directly (constitution §3) — so this test asserts
/// the *generic* catch-all's behavior using any unhandled exception as a stand-in for the real
/// concurrency exception <c>IUnitOfWork.SaveChangesAsync</c> would throw in production.
/// </summary>
public sealed class AgentResourceConflictTests
{
    private const string OwnerId = "user-1";

    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAgentPlanner _planner = Substitute.For<IAgentPlanner>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    [Fact]
    public async Task RunAsync_ShouldFailCleanlyWithARecordedError_WhenSavingAConflictingWriteThrows()
    {
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Say hello.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        _executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<AgentToolCall>());

        _planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Respond.", AgentExecutionStepType.ModelReasoning, null)]));

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Hello there!", new ChatUsage(10, 5, null, null, 100)));
        _providerResolver.Resolve(Arg.Any<string>()).Returns(aiProvider);

        // Simulate a conflicting concurrent write surfacing as an exception on the final save —
        // the same shape a real DbUpdateConcurrencyException from a raced SaveChangesAsync would take.
        var saveCallCount = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            saveCallCount++;
            if (saveCallCount == 4) // after Start/SetPlan/Step-added/Step-started — fails right when the step would complete.
            {
                throw new InvalidOperationException("Simulated concurrency conflict.");
            }

            return 1;
        });

        var mcpToolRegistry = Substitute.For<IMcpToolRegistry>();
        mcpToolRegistry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);

        var orchestrator = new AgentExecutionOrchestrator(
            _executionRepository, _agentRepository, _providerRepository, _modelRepository, _providerResolver, _planner,
            new AgentToolCatalog([], mcpToolRegistry), new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
            new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), _chatRepository, _messageRepository, _unitOfWork);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle();
        execution.TerminationReason.Should().NotBeNullOrEmpty();
    }
}
