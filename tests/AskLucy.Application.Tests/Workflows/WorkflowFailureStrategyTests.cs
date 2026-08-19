using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>spec.md User Story 7 (FR-039) — one test per workflow-level failure strategy (Stop/Continue/Retry/Fallback/Compensate), applied once a failed node's own retries (if any) are exhausted.</summary>
public sealed class WorkflowFailureStrategyTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));

    public WorkflowFailureStrategyTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private sealed class FakeExecutor(WorkflowNodeType nodeType, bool succeeds, string? retryPolicyJson = null) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(succeeds
                ? WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { ok = true }))
                : WorkflowNodeExecutionResult.Failure("Simulated failure."));
        }
    }

    /// <summary>Fails once then succeeds — used by the Retry strategy test to prove the workflow-level bonus attempt is what saves it.</summary>
    private sealed class FailsOnceExecutor(WorkflowNodeType nodeType) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(InvocationCount == 1
                ? WorkflowNodeExecutionResult.Failure("Simulated failure.")
                : WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { ok = true })));
        }
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), new AgentToolCatalog([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string? retryPolicyJson = null, string? compensatingNodeKey = null) => new(
        key, type, key, null, "{}", "{}", "{}", "[]", null, retryPolicyJson, WorkflowNodeApprovalPolicy.NeverRequire, null, compensatingNodeKey, 0, 0);

    private (WorkflowExecution Execution, WorkflowVersion Version) RegisterExecution(Workflow workflow, WorkflowVersion version)
    {
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldFailTheExecution_UnderTheStopStrategy()
    {
        var workflow = Workflow.Create(OwnerId, "Stop Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("failing", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "failing", null, null), new WorkflowConnectionSpec("failing", "end", null, null)],
            [], "{}", "{}", """{"strategy":"Stop"}""", "{}", "{}", null, OwnerId);
        var (execution, _) = RegisterExecution(workflow, version);
        var executor = new FakeExecutor(WorkflowNodeType.Transform, succeeds: false);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteTheExecution_UnderTheContinueStrategy_TreatingTheNodeFailureAsTolerated()
    {
        var workflow = Workflow.Create(OwnerId, "Continue Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("failing", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "failing", null, null), new WorkflowConnectionSpec("failing", "end", null, null)],
            [], "{}", "{}", """{"strategy":"Continue"}""", "{}", "{}", null, OwnerId);
        var (execution, version2) = RegisterExecution(workflow, version);
        var executor = new FakeExecutor(WorkflowNodeType.Transform, succeeds: false);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        var failingNodeId = version2.Nodes.Single(n => n.NodeKey == "failing").Id;
        execution.Nodes.Single(n => n.WorkflowNodeId == failingNodeId).Status.Should().Be(WorkflowExecutionNodeStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_ShouldGrantOneBonusAttempt_UnderTheRetryStrategy()
    {
        var workflow = Workflow.Create(OwnerId, "Retry Strategy Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("flaky", WorkflowNodeType.Transform, """{"maxAttempts":1,"initialDelaySeconds":0}"""), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "flaky", null, null), new WorkflowConnectionSpec("flaky", "end", null, null)],
            [], "{}", "{}", """{"strategy":"Retry"}""", "{}", "{}", null, OwnerId);
        var (execution, _) = RegisterExecution(workflow, version);
        var executor = new FailsOnceExecutor(WorkflowNodeType.Transform);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(2); // 1 configured attempt + 1 workflow-level bonus.
    }

    [Fact]
    public async Task RunAsync_ShouldRunTheAlternateNode_UnderTheFallbackStrategy()
    {
        var workflow = Workflow.Create(OwnerId, "Fallback Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("failing", WorkflowNodeType.Transform, compensatingNodeKey: "alternate"),
                Node("alternate", WorkflowNodeType.Transform),
                Node("end", WorkflowNodeType.End),
            ],
            [new WorkflowConnectionSpec("start", "failing", null, null), new WorkflowConnectionSpec("alternate", "end", null, null)],
            [], "{}", "{}", """{"strategy":"Fallback"}""", "{}", "{}", null, OwnerId);
        var (execution, version2) = RegisterExecution(workflow, version);
        var registry = new WorkflowNodeExecutorRegistry([
            new SplitExecutor(new Dictionary<string, IWorkflowNodeExecutor>
            {
                ["failing"] = new FakeExecutor(WorkflowNodeType.Transform, succeeds: false),
                ["alternate"] = new FakeExecutor(WorkflowNodeType.Transform, succeeds: true),
            }),
        ]);

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        var statusByKey = execution.Nodes.ToDictionary(n => version2.Nodes.First(vn => vn.Id == n.WorkflowNodeId).NodeKey, n => n.Status);
        statusByKey["failing"].Should().Be(WorkflowExecutionNodeStatus.Failed);
        statusByKey["alternate"].Should().Be(WorkflowExecutionNodeStatus.Completed);
        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_ShouldRunTheCompensatingNode_UnderTheCompensateStrategy_ThenStillFail()
    {
        var workflow = Workflow.Create(OwnerId, "Compensate Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("nodeA", WorkflowNodeType.Transform, compensatingNodeKey: "undoA"),
                Node("nodeB", WorkflowNodeType.Transform),
                Node("undoA", WorkflowNodeType.Transform),
                Node("end", WorkflowNodeType.End),
            ],
            [new WorkflowConnectionSpec("start", "nodeA", null, null), new WorkflowConnectionSpec("nodeA", "nodeB", null, null), new WorkflowConnectionSpec("nodeB", "end", null, null)],
            [], "{}", "{}", """{"strategy":"Compensate"}""", "{}", "{}", null, OwnerId);
        var (execution, version2) = RegisterExecution(workflow, version);

        var registry = new WorkflowNodeExecutorRegistry([
            new SplitExecutor(new Dictionary<string, IWorkflowNodeExecutor>
            {
                ["nodeA"] = new FakeExecutor(WorkflowNodeType.Transform, succeeds: true),
                ["nodeB"] = new FakeExecutor(WorkflowNodeType.Transform, succeeds: false),
                ["undoA"] = new FakeExecutor(WorkflowNodeType.Transform, succeeds: true),
            }),
        ]);

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        var undoANodeId = version2.Nodes.Single(n => n.NodeKey == "undoA").Id;
        execution.Nodes.Should().Contain(n => n.WorkflowNodeId == undoANodeId && n.Status == WorkflowExecutionNodeStatus.Completed);
    }

    /// <summary>Routes to a different fake per node key (via <see cref="WorkflowNodeExecutionContext.Node"/>'s <c>NodeKey</c>) — the Compensate test needs nodeA to succeed while nodeB fails, which a single shared executor instance can't express.</summary>
    private sealed class SplitExecutor(IReadOnlyDictionary<string, IWorkflowNodeExecutor> byNodeKey) : IWorkflowNodeExecutor
    {
        public WorkflowNodeType NodeType => WorkflowNodeType.Transform;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            byNodeKey[context.Node.NodeKey].ExecuteAsync(context, input, cancellationToken);
    }
}
