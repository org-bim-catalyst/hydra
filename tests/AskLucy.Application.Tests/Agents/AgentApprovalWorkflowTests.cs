using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.ApproveAgentAction;
using AskLucy.Application.Agents.Commands.RejectAgentAction;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md User Story 3 (FR-025-FR-028) end to end: a High/Critical-risk tool call pauses
/// execution for approval unless a matching administrator policy lets it proceed, the decision is
/// recorded either way, and an approved execution resumes exactly where it paused — never
/// re-planning or re-running an already-completed step (FR-017).
/// </summary>
public sealed class AgentApprovalWorkflowTests
{
    private const string OwnerId = "user-1";
    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    private readonly IAgentExecutionRepository _executionRepository = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAgentPlanner _planner = Substitute.For<IAgentPlanner>();
    private readonly IAgentPolicyRepository _policyRepository = Substitute.For<IAgentPolicyRepository>();
    private readonly IUserChatRepository _chatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IAgentTool _highRiskTool = Substitute.For<IAgentTool>();
    private readonly List<AgentToolCall> _recordedToolCalls = [];

    public AgentApprovalWorkflowTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _providerResolver.Resolve(Arg.Any<string>()).Returns(_aiProvider);

        _highRiskTool.Name.Returns("FakeHighRiskTool");
        _highRiskTool.RiskLevel.Returns(AgentToolRiskLevel.High);
        _highRiskTool.RequiredPermissions.Returns([AgentToolPermission.HighRiskOperation]);
        _highRiskTool.InputSchemaJson.Returns("{}");
        _highRiskTool.OutputSchemaJson.Returns("{}");
        _highRiskTool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { simulated = true })));

        _executionRepository.AddToolCall(Arg.Do<AgentToolCall>(tc => _recordedToolCalls.Add(tc)));
        _executionRepository.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _recordedToolCalls.ToList());
    }

    private (AgentExecution Execution, AgentVersion Version) CreateExecution()
    {
        var instructions = new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null);
        var agent = Agent.Create(OwnerId, "Approval Agent", null, AgentType.Task, instructions, Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool("FakeHighRiskTool", null, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Do the risky thing.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        _agentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(version.ModelProviderId, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(version.ModelId, Arg.Any<CancellationToken>()).Returns(model);

        _planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Execute the risky action.", AgentExecutionStepType.ToolCall, "FakeHighRiskTool")]));

        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("""{"action":"read-only"}""", new ChatUsage(5, 5, null, null, 20)));

        return (execution, version);
    }

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private AgentExecutionOrchestrator CreateOrchestrator() => new(
        _executionRepository, _agentRepository, _providerRepository, _modelRepository, _providerResolver, _planner,
        new AgentToolCatalog([_highRiskTool], EmptyMcpToolRegistry()), new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
        new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(_policyRepository), Substitute.For<IAgentExecutionNotifier>(),
        Substitute.For<IAgentAuditLogRepository>(), _chatRepository, _messageRepository, _unitOfWork);

    [Fact]
    public async Task RunAsync_ShouldPauseWithAPendingApproval_WhenAHighRiskToolCallHasNoMatchingPolicy()
    {
        var (execution, _) = CreateExecution();
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([]);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.WaitingForApproval);
        execution.Approvals.Should().ContainSingle();
        var approval = execution.Approvals.Single();
        approval.Decision.Should().Be(AgentApprovalDecision.Pending);
        approval.WasPolicyBased.Should().BeFalse();
        execution.Steps.Single().Status.Should().Be(AgentExecutionStepStatus.WaitingForApproval);
        await _highRiskTool.DidNotReceive().ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldNeverPause_AndShouldRecordThePolicyMatch_WhenAnEnabledPolicyMatchesTheCall()
    {
        var (execution, _) = CreateExecution();
        var policy = AgentPolicy.Create("Always allow", null, "FakeHighRiskTool", conditionsJson: null, "admin-1");
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        await CreateOrchestrator().RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.Approvals.Should().ContainSingle();
        var approval = execution.Approvals.Single();
        approval.Decision.Should().Be(AgentApprovalDecision.Approved);
        approval.WasPolicyBased.Should().BeTrue();
        approval.MatchedAgentPolicyId.Should().Be(policy.Id);
        await _highRiskTool.Received(1).ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldResumeAndCompleteTheToolCall_WithoutReplanningOrDuplicatingSteps_AfterInteractiveApproval()
    {
        var (execution, _) = CreateExecution();
        _policyRepository.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([]);

        var orchestrator = CreateOrchestrator();
        await orchestrator.RunAsync(execution.Id, CancellationToken.None);
        execution.Status.Should().Be(AgentExecutionStatus.WaitingForApproval);

        var approval = execution.Approvals.Single();
        approval.Approve(OwnerId);
        execution.Resume();

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(AgentExecutionStatus.Completed);
        execution.Steps.Should().ContainSingle();
        execution.Approvals.Should().ContainSingle();
        await _highRiskTool.Received(1).ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
        await _planner.Received(1).CreatePlanAsync(
            Arg.Any<string>(), Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAgentActionCommandHandler_ShouldApproveAndReenqueue()
    {
        var (execution, _) = CreateExecution();
        var step = execution.AddStep(0, "Execute the risky action.", AgentExecutionStepType.ToolCall, null, "FakeHighRiskTool", null);
        var toolCall = AgentToolCall.Create(step.Id, "FakeHighRiskTool", AgentToolRiskLevel.High, "[]", "{}", wasApprovalRequired: true);
        var approval = execution.RequestApproval(toolCall.Id, "Execute FakeHighRiskTool", "{}");

        var runner = Substitute.For<IAgentExecutionRunner>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new ApproveAgentActionCommandHandler(_executionRepository, runner, Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), _unitOfWork, currentUser);

        var result = await handler.Handle(new ApproveAgentActionCommand(execution.Id, approval.Id), CancellationToken.None);

        result.Decision.Should().Be("Approved");
        execution.Status.Should().Be(AgentExecutionStatus.Running);
        await runner.Received(1).EnqueueAsync(execution.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAgentActionCommandHandler_ShouldFailTheExecution_AndNeverExecuteTheTool()
    {
        var (execution, _) = CreateExecution();
        var step = execution.AddStep(0, "Execute the risky action.", AgentExecutionStepType.ToolCall, null, "FakeHighRiskTool", null);
        var toolCall = AgentToolCall.Create(step.Id, "FakeHighRiskTool", AgentToolRiskLevel.High, "[]", "{}", wasApprovalRequired: true);
        var approval = execution.RequestApproval(toolCall.Id, "Execute FakeHighRiskTool", "{}");
        _executionRepository.GetToolCallByIdAsync(toolCall.Id, Arg.Any<CancellationToken>()).Returns(toolCall);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new RejectAgentActionCommandHandler(_executionRepository, Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), _unitOfWork, currentUser);

        var result = await handler.Handle(new RejectAgentActionCommand(execution.Id, approval.Id, "No thanks."), CancellationToken.None);

        result.Decision.Should().Be("Rejected");
        execution.Status.Should().Be(AgentExecutionStatus.Failed);
        execution.TerminationReason.Should().Contain("No thanks.");
        toolCall.FailureReason.Should().NotBeNullOrEmpty();
        step.Status.Should().Be(AgentExecutionStepStatus.Cancelled);
        await _highRiskTool.DidNotReceive().ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAgentActionCommandHandler_ShouldThrow_WhenTheApprovalWasAlreadyDecided()
    {
        var (execution, _) = CreateExecution();
        var step = execution.AddStep(0, "Execute the risky action.", AgentExecutionStepType.ToolCall, null, "FakeHighRiskTool", null);
        var toolCall = AgentToolCall.Create(step.Id, "FakeHighRiskTool", AgentToolRiskLevel.High, "[]", "{}", wasApprovalRequired: true);
        var approval = execution.RequestApproval(toolCall.Id, "Execute FakeHighRiskTool", "{}");
        approval.Approve(OwnerId);

        var runner = Substitute.For<IAgentExecutionRunner>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new ApproveAgentActionCommandHandler(_executionRepository, runner, Substitute.For<IAgentExecutionNotifier>(), Substitute.For<IAgentAuditLogRepository>(), _unitOfWork, currentUser);

        var act = () => handler.Handle(new ApproveAgentActionCommand(execution.Id, approval.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
