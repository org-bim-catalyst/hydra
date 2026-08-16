using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Commands.ApproveWorkflowNode;
using AskLucy.Application.Workflows.Commands.RejectWorkflowNode;
using AskLucy.Application.Workflows.Commands.RequestWorkflowNodeChanges;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Queries.GetWorkflowApproval;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md User Story 5 end to end: a Human Approval node (or any node wrapping a High/Critical-risk
/// capability) pauses execution for an explicit decision — or auto-proceeds under a matching
/// administrator policy — with the decision recorded either way. Covers the platform-mandatory
/// approval baseline: only the platform's own risk-based baseline can ever be matched by a
/// <see cref="WorkflowPolicy"/>, and a workflow author's own stricter, node-level opt-in can never
/// be weakened by node configuration or bypassed by a policy.
/// </summary>
public sealed class WorkflowApprovalWorkflowTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));
    private readonly IAgentTool _highRiskTool = Substitute.For<IAgentTool>();

    public WorkflowApprovalWorkflowTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _highRiskTool.Name.Returns("KnowledgeSearchTool");
        _highRiskTool.RiskLevel.Returns(AgentToolRiskLevel.High);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), new AgentToolCatalog([_highRiskTool], EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeSpec Node(
        string key, WorkflowNodeType type, string configurationJson = "{}", WorkflowNodeApprovalPolicy approvalPolicy = WorkflowNodeApprovalPolicy.NeverRequire) =>
        new(key, type, key, null, "{}", "{}", configurationJson, "[]", null, null, approvalPolicy, null, null, 0, 0);

    private sealed class FakeExecutor(WorkflowNodeType nodeType) : IWorkflowNodeExecutor
    {
        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { ok = true })));
    }

    /// <summary>Start → HumanApproval → End.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpHumanApprovalWorkflow()
    {
        var workflow = Workflow.Create(OwnerId, "Approval Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("approval", WorkflowNodeType.HumanApproval), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "approval", null, null), new WorkflowConnectionSpec("approval", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        return RegisterExecution(workflow, version);
    }

    /// <summary>Start → RagSearch (mapped to the High-risk "KnowledgeSearchTool") → End.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpHighRiskCapabilityWorkflow(WorkflowNodeApprovalPolicy approvalPolicy = WorkflowNodeApprovalPolicy.NeverRequire)
    {
        var workflow = Workflow.Create(OwnerId, "High-Risk Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("rag", WorkflowNodeType.RagSearch, approvalPolicy: approvalPolicy), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "rag", null, null), new WorkflowConnectionSpec("rag", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        return RegisterExecution(workflow, version);
    }

    private (WorkflowExecution Execution, WorkflowVersion Version) RegisterExecution(Workflow workflow, WorkflowVersion version)
    {
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldPauseWithAPendingApproval_ForAHumanApprovalNode()
    {
        var (execution, _) = SetUpHumanApprovalWorkflow();

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        execution.Approvals.Should().ContainSingle();
        var approval = execution.Approvals.Single();
        approval.Decision.Should().Be(WorkflowApprovalDecision.Pending);
        approval.WasPolicyBased.Should().BeFalse();
        execution.Nodes.Should().ContainSingle(n => n.Status == WorkflowExecutionNodeStatus.WaitingForApproval);
    }

    [Fact]
    public async Task RunAsync_ShouldPauseWithAPendingApproval_ForAHighRiskCapabilityNode_EvenWhenTheNodeItselfNeverRequiresApproval()
    {
        // The node's own ApprovalPolicy is NeverRequire — proves the platform-mandatory,
        // risk-based baseline can never be weakened by the workflow author's own configuration.
        var (execution, _) = SetUpHighRiskCapabilityWorkflow(WorkflowNodeApprovalPolicy.NeverRequire);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([new FakeExecutor(WorkflowNodeType.RagSearch)])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        execution.Approvals.Should().ContainSingle();
        execution.Approvals.Single().Decision.Should().Be(WorkflowApprovalDecision.Pending);
    }

    [Fact]
    public async Task RunAsync_ShouldNeverPause_AndShouldRecordThePolicyMatch_WhenAnEnabledWorkflowPolicyMatchesTheHighRiskNode()
    {
        var (execution, _) = SetUpHighRiskCapabilityWorkflow();
        var policy = WorkflowPolicy.Create("Always allow knowledge search", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", conditionsJson: null, "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([policy]);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([new FakeExecutor(WorkflowNodeType.RagSearch)])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.Approvals.Should().ContainSingle();
        var approval = execution.Approvals.Single();
        approval.Decision.Should().Be(WorkflowApprovalDecision.Approve);
        approval.WasPolicyBased.Should().BeTrue();
        approval.MatchedWorkflowPolicyId.Should().Be(policy.Id);
    }

    [Fact]
    public async Task RunAsync_ShouldPause_AndShouldNeverQueryPolicies_WhenOnlyTheAuthorsOwnApprovalPolicyForcesApproval()
    {
        // Transform is neither a Human Approval node nor a High/Critical-risk capability — only the
        // node's own AlwaysRequire config triggers the pause. A WorkflowPolicy can only ever match
        // the platform's own risk-based baseline, never an author's stricter opt-in, so the policy
        // repository must not even be queried for this node.
        var workflow = Workflow.Create(OwnerId, "Author Forced Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("transform", WorkflowNodeType.Transform, """{"expression":"1","outputField":"result"}""", WorkflowNodeApprovalPolicy.AlwaysRequire),
                Node("end", WorkflowNodeType.End),
            ],
            [new WorkflowConnectionSpec("start", "transform", null, null), new WorkflowConnectionSpec("transform", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        var (execution, _) = RegisterExecution(workflow, version);

        var registry = new WorkflowNodeExecutorRegistry([new TransformNodeExecutor(_expressionEvaluator)]);
        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        await _policyRepository.DidNotReceive().ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldResumeAndCompleteTheHighRiskNode_WithoutDuplicatingRows_AfterInteractiveApproval()
    {
        var (execution, _) = SetUpHighRiskCapabilityWorkflow();
        var orchestrator = CreateOrchestrator(new WorkflowNodeExecutorRegistry([new FakeExecutor(WorkflowNodeType.RagSearch)]));

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);
        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);

        var approval = execution.Approvals.Single();
        approval.Approve(OwnerId);
        execution.Resume();

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.Nodes.Should().HaveCount(3);
        execution.Approvals.Should().ContainSingle();
    }

    [Fact]
    public async Task ApproveWorkflowNodeCommandHandler_ShouldApproveAndReenqueue()
    {
        var (execution, version) = SetUpHighRiskCapabilityWorkflow();
        var ragNodeId = version.Nodes.Single(n => n.NodeKey == "rag").Id;
        var executionNode = execution.AddNode(ragNodeId);
        executionNode.WaitForApproval();
        var approval = execution.RequestApproval(executionNode.Id, "Execute KnowledgeSearchTool at step 'rag'", "{}", null);

        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new ApproveWorkflowNodeCommandHandler(_executionRepository, runner, Substitute.For<IWorkflowAuditLogRepository>(), _unitOfWork, currentUser);

        var result = await handler.Handle(new ApproveWorkflowNodeCommand(execution.Id, approval.Id), CancellationToken.None);

        result.Decision.Should().Be("Approve");
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
        await runner.Received(1).EnqueueAsync(execution.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveWorkflowNodeCommandHandler_ShouldThrow_WhenTheApprovalWasAlreadyDecided()
    {
        var (execution, version) = SetUpHighRiskCapabilityWorkflow();
        var ragNodeId = version.Nodes.Single(n => n.NodeKey == "rag").Id;
        var executionNode = execution.AddNode(ragNodeId);
        executionNode.WaitForApproval();
        var approval = execution.RequestApproval(executionNode.Id, "Execute KnowledgeSearchTool at step 'rag'", "{}", null);
        approval.Approve(OwnerId);

        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new ApproveWorkflowNodeCommandHandler(_executionRepository, runner, Substitute.For<IWorkflowAuditLogRepository>(), _unitOfWork, currentUser);

        var act = () => handler.Handle(new ApproveWorkflowNodeCommand(execution.Id, approval.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task RejectWorkflowNodeCommandHandler_ShouldFailTheExecution_AndNeverReenqueue()
    {
        var (execution, version) = SetUpHighRiskCapabilityWorkflow();
        var ragNodeId = version.Nodes.Single(n => n.NodeKey == "rag").Id;
        var executionNode = execution.AddNode(ragNodeId);
        executionNode.WaitForApproval();
        var approval = execution.RequestApproval(executionNode.Id, "Execute KnowledgeSearchTool at step 'rag'", "{}", null);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new RejectWorkflowNodeCommandHandler(_executionRepository, Substitute.For<IWorkflowAuditLogRepository>(), _unitOfWork, currentUser);

        var result = await handler.Handle(new RejectWorkflowNodeCommand(execution.Id, approval.Id, "No thanks."), CancellationToken.None);

        result.Decision.Should().Be("Reject");
        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.TerminationReason.Should().Contain("No thanks.");
        executionNode.Status.Should().Be(WorkflowExecutionNodeStatus.Failed);
    }

    [Fact]
    public async Task RequestWorkflowNodeChangesCommandHandler_ShouldFailTheExecution_AndRecordTheComments()
    {
        var (execution, version) = SetUpHighRiskCapabilityWorkflow();
        var ragNodeId = version.Nodes.Single(n => n.NodeKey == "rag").Id;
        var executionNode = execution.AddNode(ragNodeId);
        executionNode.WaitForApproval();
        var approval = execution.RequestApproval(executionNode.Id, "Execute KnowledgeSearchTool at step 'rag'", "{}", null);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new RequestWorkflowNodeChangesCommandHandler(_executionRepository, Substitute.For<IWorkflowAuditLogRepository>(), _unitOfWork, currentUser);

        var result = await handler.Handle(new RequestWorkflowNodeChangesCommand(execution.Id, approval.Id, "Please narrow the query."), CancellationToken.None);

        result.Decision.Should().Be("RequestChanges");
        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.TerminationReason.Should().Contain("Please narrow the query.");
    }

    [Fact]
    public async Task GetWorkflowApprovalQueryHandler_ShouldReturnTheApproval()
    {
        var (execution, version) = SetUpHighRiskCapabilityWorkflow();
        var ragNodeId = version.Nodes.Single(n => n.NodeKey == "rag").Id;
        var executionNode = execution.AddNode(ragNodeId);
        executionNode.WaitForApproval();
        var approval = execution.RequestApproval(executionNode.Id, "Execute KnowledgeSearchTool at step 'rag'", "{}", null);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        var handler = new GetWorkflowApprovalQueryHandler(_executionRepository, currentUser);

        var result = await handler.Handle(new GetWorkflowApprovalQuery(execution.Id, approval.Id), CancellationToken.None);

        result.Id.Should().Be(approval.Id);
        result.Decision.Should().Be("Pending");
    }
}
