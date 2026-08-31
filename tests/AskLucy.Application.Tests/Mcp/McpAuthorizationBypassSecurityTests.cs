using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md Security Tests "Authorization bypass" matrix. Most of it is already covered and
/// cross-referenced here rather than duplicated: a non-administrator attempting server management
/// (<c>McpServersControllerTests</c>'s <c>ShouldReturn403_WhenCallerHasNoAdminRole</c> across all
/// 16 admin routes) and cross-user execution-history scoping (spec 020's existing, unmodified
/// per-user execution scoping — MCP tool calls ride the same <c>AgentExecution.RunByUserId</c>
/// boundary as any native tool call, no MCP-specific code touches it). This file covers the two
/// MCP-specific angles: a `PendingReview`/`Deactivated` tool is unreachable at execution time
/// (FR-021/FR-022), and MCP execution paths cannot reach Knowledge Base/file/memory data the
/// requesting user isn't already authorized for (FR-034) because no such dependency exists to
/// call through.
/// </summary>
public sealed class McpAuthorizationBypassSecurityTests
{
    private const string OwnerId = "user-1";
    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    [Theory]
    [InlineData(typeof(McpToolAdapter))]
    [InlineData(typeof(McpResourceReadTool))]
    public void McpExecutionPaths_ShouldHaveNoDependencyOnKnowledgeBaseFileOrMemoryAbstractions(Type toolType)
    {
        // FR-034 — structurally guaranteed: neither class can reach Knowledge Base/file/memory
        // data, because nothing to call through exists in its dependency graph. There is exactly
        // one constructor on each (primary constructor), so this is exhaustive.
        var parameterTypeNames = toolType.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);
        parameterTypeNames.Should().NotContain(name =>
            name.Contains("KnowledgeBase") || name.Contains("Retrieval") || name.Contains("Rag") ||
            name.Contains("File") || name.Contains("Memory"));
    }

    [Fact]
    public async Task RunAsync_ShouldFailTheExecutionWithARecordedError_RatherThanCallTheTool_WhenThePlanReferencesATool_NotInTheActiveCatalog()
    {
        // Simulates a PendingReview/Deactivated MCP tool: it's configured on the agent
        // (AgentTool/ToolsSnapshotJson) but absent from IMcpToolRegistry.ActiveTools — exactly
        // what ListActiveAvailableAsync's ActivationStatus == Active filter produces for a tool
        // that hasn't been (or is no longer) activated. The orchestrator must never call it, even
        // if the planner names it, and must fail loudly rather than silently succeed or vanish.
        var namespacedName = $"mcp:{Guid.NewGuid()}:deleteRecord";

        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var agentRepository = Substitute.For<IAgentRepository>();
        var providerRepository = Substitute.For<IAIProviderRepository>();
        var modelRepository = Substitute.For<IAIModelRepository>();
        var providerResolver = Substitute.For<IAIProviderResolver>();
        var planner = Substitute.For<IAgentPlanner>();
        var policyRepository = Substitute.For<IAgentPolicyRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]); // the PendingReview tool is NOT active

        var instructions = new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null);
        var agent = Agent.Create(OwnerId, "Bypass Attempt Agent", null, AgentType.Task, instructions, Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool(namespacedName, null, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Delete the record.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<AgentToolCall>());
        providerResolver.Resolve(Arg.Any<string>()).Returns(Substitute.For<IAIProvider>());

        // A hallucinating/compromised planner still names the (unavailable) tool — the plan step
        // itself is attacker-adjacent input the orchestrator must not trust blindly.
        planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Delete the record.", AgentExecutionStepType.ToolCall, namespacedName)]));

        var orchestrator = new AgentExecutionOrchestrator(
            executionRepository, agentRepository, providerRepository, modelRepository, providerResolver, planner,
            new AgentToolCatalog([], registry), new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
            new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(policyRepository), Substitute.For<IAgentExecutionNotifier>(),
            Substitute.For<IAgentAuditLogRepository>(), Substitute.For<IUserChatRepository>(), Substitute.For<IMessageRepository>(), unitOfWork);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        // Never silently succeeds and never calls anything — the planner never even receives this
        // tool as an option (availableTools excludes it), and the plan-step lookup that would
        // otherwise try to execute it fails loudly instead.
        execution.Status.Should().Be(AgentExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle();
        await planner.Received(1).CreatePlanAsync(
            Arg.Any<string>(), Arg.Any<AgentInstructions>(), Arg.Is<IReadOnlyList<IAgentTool>>(tools => tools!.Count == 0),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
