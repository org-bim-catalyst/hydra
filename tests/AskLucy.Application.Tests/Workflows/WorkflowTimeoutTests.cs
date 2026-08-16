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

/// <summary>
/// spec.md User Story 7 (FR-041) — a per-node timeout (a linked <see cref="CancellationTokenSource"/>
/// scoped to <see cref="WorkflowNode.TimeoutSeconds"/> or the system-wide default) records <see
/// cref="WorkflowErrorCategory.Timeout"/> and is itself subject to the node's own retry policy.
/// Workflow-level <c>MaxExecutionDurationSeconds</c> (an existing <c>WorkflowBudgetGuard</c> check
/// since User Story 1) and Human Approval timeout (<c>WorkflowApprovalTimeoutTests</c>, User Story 5)
/// are covered elsewhere — this file is scoped to the node-level timeout mechanism this story adds.
/// </summary>
public sealed class WorkflowTimeoutTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));

    public WorkflowTimeoutTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private sealed class NeverFinishingExecutor(WorkflowNodeType nodeType) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable — the delay above always throws OperationCanceledException first.");
        }
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), new AgentToolCatalog([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, int? timeoutSeconds = null, string? retryPolicyJson = null) => new(
        key, type, key, null, "{}", "{}", "{}", "[]", timeoutSeconds, retryPolicyJson, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpWorkflow(WorkflowNodeSpec middleNode)
    {
        var workflow = Workflow.Create(OwnerId, "Timeout Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), middleNode, Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", middleNode.NodeKey, null, null), new WorkflowConnectionSpec(middleNode.NodeKey, "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldFailTheNode_WhenItExceedsItsConfiguredTimeout()
    {
        var middleNode = Node("hangs", WorkflowNodeType.Transform, timeoutSeconds: 1);
        var (execution, version) = SetUpWorkflow(middleNode);
        var executor = new NeverFinishingExecutor(WorkflowNodeType.Transform);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle(e => e.Category == WorkflowErrorCategory.Timeout);
        var nodeId = version.Nodes.Single(n => n.NodeKey == "hangs").Id;
        execution.Nodes.Single(n => n.WorkflowNodeId == nodeId).Status.Should().Be(WorkflowExecutionNodeStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_ShouldRetryATimedOutNode_WhenItsRetryPolicyAllows()
    {
        var middleNode = Node("hangs-retryable", WorkflowNodeType.Transform, timeoutSeconds: 1, retryPolicyJson: """{"maxAttempts":2,"initialDelaySeconds":0}""");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new NeverFinishingExecutor(WorkflowNodeType.Transform);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        executor.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldNotTreatAnOuterCancellation_AsARetryableNodeTimeout()
    {
        // maxAttempts=2 would retry a genuine per-node timeout, but a cancellation of the caller's
        // own token (not the per-node CancelAfter) must propagate straight out of the retry loop —
        // never mistaken for a retryable timeout — and is caught once, at the top level, per the
        // class's own documented failure-handling contract (never rethrown to the caller).
        var middleNode = Node("hangs-cancelled", WorkflowNodeType.Transform, timeoutSeconds: 60, retryPolicyJson: """{"maxAttempts":2,"initialDelaySeconds":0}""");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new NeverFinishingExecutor(WorkflowNodeType.Transform);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, cts.Token);

        executor.InvocationCount.Should().Be(1);
        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
    }
}
