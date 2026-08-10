using System.Text.Json;
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
/// research.md Decision 12/SC-007: an `isTestExecution: true` run with a mutating tool
/// (`MemoryWriteTool`'s own `ModifyData` permission, mirrored here via a fake) configured never
/// calls that tool at all — the step is recorded `Skipped`, not gated behind an inert approval.
/// </summary>
public sealed class TestExecutionSkipsMutatingToolsTests
{
    private const string OwnerId = "user-1";
    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    [Fact]
    public async Task RunAsync_ShouldNeverExecuteAMutatingTool_AndShouldRecordTheStepSkipped_ForATestExecution()
    {
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var agentRepository = Substitute.For<IAgentRepository>();
        var providerRepository = Substitute.For<IAIProviderRepository>();
        var modelRepository = Substitute.For<IAIModelRepository>();
        var providerResolver = Substitute.For<IAIProviderResolver>();
        var planner = Substitute.For<IAgentPlanner>();
        var chatRepository = Substitute.For<IUserChatRepository>();
        var messageRepository = Substitute.For<IMessageRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var aiProvider = Substitute.For<IAIProvider>();

        var mutatingTool = Substitute.For<IAgentTool>();
        mutatingTool.Name.Returns("MemoryWriteTool");
        mutatingTool.RiskLevel.Returns(AgentToolRiskLevel.Medium);
        mutatingTool.RequiredPermissions.Returns([AgentToolPermission.ModifyData]);

        var instructions = new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null);
        var agent = Agent.Create(OwnerId, "Memory Agent", null, AgentType.Task, instructions, Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool("MemoryWriteTool", null, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Remember something.", isTestExecution: true, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);
        executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<AgentToolCall>());
        providerResolver.Resolve(Arg.Any<string>()).Returns(aiProvider);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Remember the fact.", AgentExecutionStepType.ToolCall, "MemoryWriteTool")]));

        var orchestrator = new AgentExecutionOrchestrator(
            executionRepository, agentRepository, providerRepository, modelRepository, providerResolver, planner,
            new AgentToolCatalog([mutatingTool]), new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
            new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), chatRepository, messageRepository, unitOfWork);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.Steps.Should().ContainSingle();
        var step = execution.Steps.Single();
        step.Status.Should().Be(AgentExecutionStepStatus.Skipped);
        step.OutputJson.Should().Be("write actions are disabled for test executions");

        await mutatingTool.DidNotReceive().ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
        executionRepository.DidNotReceive().AddToolCall(Arg.Any<AgentToolCall>());
        execution.Approvals.Should().BeEmpty();
    }
}
