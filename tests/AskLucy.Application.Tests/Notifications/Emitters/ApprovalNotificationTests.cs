using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Tests.Workflows;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications.Emitters;

/// <summary>
/// T143 (specs/067 US5) — both orchestrators tell the execution's owner, and only the owner, when a run waits for a
/// person to decide: one publish per approval, keyed by the approval, with a route that opens that approval.
/// </summary>
public sealed class ApprovalNotificationTests
{
    private const string OwnerId = "user-1";
    private static readonly AIModelCapabilities NoCapabilities = new(true, false, false, false, false, false, false, false, false);

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();

    public ApprovalNotificationTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
    }

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    // ---- agents ----

    private readonly IAgentExecutionRepository _agentExecutions = Substitute.For<IAgentExecutionRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IAgentPolicyRepository _agentPolicies = Substitute.For<IAgentPolicyRepository>();
    private readonly IAgentTool _highRiskTool = Substitute.For<IAgentTool>();
    private readonly List<AgentToolCall> _toolCalls = [];

    private (AgentExecution Execution, Agent Agent) SetUpAgentExecution()
    {
        _highRiskTool.Name.Returns("FakeHighRiskTool");
        _highRiskTool.RiskLevel.Returns(AgentToolRiskLevel.High);
        _highRiskTool.RequiredPermissions.Returns([AgentToolPermission.HighRiskOperation]);
        _highRiskTool.InputSchemaJson.Returns("{}");
        _highRiskTool.OutputSchemaJson.Returns("{}");
        _highRiskTool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { simulated = true })));
        _agentExecutions.AddToolCall(Arg.Do<AgentToolCall>(tc => _toolCalls.Add(tc)));
        _agentExecutions.ListToolCallsByStepIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => _toolCalls.ToList());

        var agent = Agent.Create(
            OwnerId, "Risky Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);
        agent.AddTool("FakeHighRiskTool", null, OwnerId);
        var version = agent.Publish(null, OwnerId);
        var execution = AgentExecution.Create(agent.Id, version.Id, OwnerId, "Do the risky thing.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(provider.Id, "gpt-5", "GPT-5", 128_000, 4096, NoCapabilities, null, null, "system");
        _agentExecutions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _agents.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _agents.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        var providers = Substitute.For<IAIProviderRepository>();
        providers.GetByIdAsync(version.ModelProviderId!.Value, Arg.Any<CancellationToken>()).Returns(provider);
        var models = Substitute.For<IAIModelRepository>();
        models.GetByIdAsync(version.ModelId!.Value, Arg.Any<CancellationToken>()).Returns(model);

        var planner = Substitute.For<IAgentPlanner>();
        planner.CreatePlanAsync(
                execution.Objective, Arg.Any<AgentInstructions>(), Arg.Any<IReadOnlyList<IAgentTool>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPlan(execution.Objective, [new AgentPlanStep(0, "Execute the risky action.", AgentExecutionStepType.ToolCall, "FakeHighRiskTool")]));
        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("""{"action":"read-only"}""", new ChatUsage(5, 5, null, null, 20)));
        var resolver = Substitute.For<IAIProviderResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(aiProvider);

        _agentOrchestrator = new AgentExecutionOrchestrator(
            _agentExecutions, _agents, providers, models, resolver, planner,
            new AgentToolCatalog([_highRiskTool], EmptyMcpToolRegistry()),
            new AgentBudgetGuard(Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions())),
            new AgentDuplicateToolCallDetector(), new AgentPolicyEvaluator(_agentPolicies), Substitute.For<IAgentExecutionNotifier>(),
            Substitute.For<IAgentAuditLogRepository>(), Substitute.For<IUserChatRepository>(), Substitute.For<IMessageRepository>(), _publisher, _unitOfWork);

        return (execution, agent);
    }

    private AgentExecutionOrchestrator _agentOrchestrator = null!;

    [Fact]
    public async Task Agent_RequestApproval_PublishesOnceToTheOwner_KeyedByTheApproval()
    {
        var (execution, agent) = SetUpAgentExecution();
        _agentPolicies.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([]);

        await _agentOrchestrator.RunAsync(execution.Id, TestContext.Current.CancellationToken);

        var approval = execution.Approvals.Single();
        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.AgentApprovalRequested &&
            ((NotificationRecipient.User)r.Recipient).UserId == OwnerId &&
            r.RelatedItem == new RelatedItem("AgentExecution", execution.Id.ToString(), agent.Id.ToString()) &&
            r.EventKey == $"agent-approval:{approval.Id}:requested" &&
            r.Variables["approvalId"] == approval.Id.ToString() &&
            r.Variables["agentName"] == "Risky Agent" &&
            r.Variables["intendedAction"] == approval.IntendedActionDescription));
    }

    [Fact]
    public async Task Agent_AnApprovalAnAdministratorPolicyGrants_NotifiesNobody()
    {
        var (execution, _) = SetUpAgentExecution();
        var policy = AgentPolicy.Create("Always allow", null, "FakeHighRiskTool", conditionsJson: null, "admin-1");
        _agentPolicies.ListEnabledByToolNameAsync("FakeHighRiskTool", Arg.Any<CancellationToken>()).Returns([policy]);

        await _agentOrchestrator.RunAsync(execution.Id, TestContext.Current.CancellationToken);

        _publisher.DidNotReceive().Publish(Arg.Is<NotificationRequest>(r => r != null && r!.Type == NotificationTypeKeys.AgentApprovalRequested));
    }

    [Fact]
    public async Task Agent_TheApprovalRouteCarriesTheApprovalId()
    {
        var route = NotificationTypeCatalog.Get(NotificationTypeKeys.AgentApprovalRequested).RouteTemplate;

        route.Should().Be("/agents/{parentId}/executions/{id}?approval={approvalId}");
        await Task.CompletedTask;
    }

    // ---- workflows ----

    private readonly IWorkflowExecutionRepository _workflowExecutions = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflows = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _workflowPolicies = Substitute.For<IWorkflowPolicyRepository>();

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type) =>
        new(key, type, key, null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    private (WorkflowExecution Execution, Workflow Workflow, WorkflowExecutionOrchestrator Orchestrator) SetUpWorkflowExecution()
    {
        _workflowPolicies.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
        var workflow = Workflow.Create(OwnerId, "Approval Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("approval", WorkflowNodeType.HumanApproval), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "approval", null, null), new WorkflowConnectionSpec("approval", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _workflowExecutions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflows.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _workflows.GetByIdAsync(workflow.Id, Arg.Any<CancellationToken>()).Returns(workflow);

        var evaluator = new WorkflowExpressionEvaluator();
        var orchestrator = new WorkflowExecutionOrchestrator(
            _workflowExecutions, _workflows, new WorkflowNodeExecutorRegistry([]), evaluator,
            new WorkflowBudgetGuard(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions())),
            new WorkflowPolicyEvaluator(_workflowPolicies), new AgentToolCatalog([], EmptyMcpToolRegistry()),
            WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _publisher, _unitOfWork);
        return (execution, workflow, orchestrator);
    }

    [Fact]
    public async Task Workflow_RequestApproval_PublishesOnceToTheOwner_KeyedByTheApproval()
    {
        var (execution, workflow, orchestrator) = SetUpWorkflowExecution();

        await orchestrator.RunAsync(execution.Id, TestContext.Current.CancellationToken);

        var approval = execution.Approvals.Single();
        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.WorkflowApprovalRequested &&
            ((NotificationRecipient.User)r.Recipient).UserId == OwnerId &&
            r.RelatedItem == new RelatedItem("WorkflowExecution", execution.Id.ToString(), workflow.Id.ToString()) &&
            r.EventKey == $"workflow-approval:{approval.Id}:requested" &&
            r.Variables["approvalId"] == approval.Id.ToString() &&
            r.Variables["workflowName"] == "Approval Workflow" &&
            r.Variables["nodeName"] == "approval" &&
            r.Variables["intendedAction"] == approval.IntendedActionDescription));
    }

    [Fact]
    public async Task Workflow_ARunThatResumesWhileStillPending_DoesNotPublishAgain()
    {
        var (execution, _, orchestrator) = SetUpWorkflowExecution();

        await orchestrator.RunAsync(execution.Id, TestContext.Current.CancellationToken);
        await orchestrator.RunAsync(execution.Id, TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null && r!.Type == NotificationTypeKeys.WorkflowApprovalRequested));
    }

    [Fact]
    public void Workflow_TheApprovalRouteCarriesTheApprovalId() =>
        NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowApprovalRequested).RouteTemplate
            .Should().Be("/workflows/{parentId}/executions/{id}?approval={approvalId}");
}
