using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md User Story 7 (FR-043, research.md Decision 13) — a mutating node that declares an
/// <see cref="WorkflowNode.IdempotencyKeyExpression"/> resolves that key before invoking its
/// underlying capability again; a match against a prior <see cref="WorkflowExecutionNodeStatus.Completed"/>
/// row for the same <see cref="WorkflowNode"/> in this execution reuses that row's output instead of
/// re-invoking — the external side effect happens exactly once even though the node is dispatched
/// more than once (here, by a bounded loop revisiting the same mutating node every iteration).
/// </summary>
public sealed class WorkflowIdempotencyTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));

    public WorkflowIdempotencyTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private sealed class CountingExecutor(WorkflowNodeType nodeType) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { call = InvocationCount })));
        }
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry, AgentToolCatalog toolCatalog) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), toolCatalog, WorkflowOrchestratorTestHelpers.NoOpNotifier(),
        WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    /// <summary>Start → loopBody (NativeTool, mutating) → [loop-back / end], bounded at 2 iterations.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version, AgentToolCatalog ToolCatalog) SetUpMutatingLoopWorkflow(string? idempotencyKeyExpression)
    {
        var mutatingTool = Substitute.For<IAgentTool>();
        mutatingTool.Name.Returns("FakeMutatingTool");
        mutatingTool.RiskLevel.Returns(AgentToolRiskLevel.Low);
        mutatingTool.RequiredPermissions.Returns([AgentToolPermission.ModifyData]);
        var toolCatalog = new AgentToolCatalog([mutatingTool], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());

        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}", string? idempotencyExpr = null) =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, idempotencyExpr, null, 0, 0);

        var workflow = Workflow.Create(OwnerId, "Idempotency Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("loopBody", WorkflowNodeType.NativeTool, """{"toolName":"FakeMutatingTool","maxIterations":2}""", idempotencyKeyExpression),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "loopBody", null, null),
                new WorkflowConnectionSpec("loopBody", "loopBody", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowConnectionSpec("loopBody", "end", null, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version, toolCatalog);
    }

    [Fact]
    public async Task RunAsync_ShouldInvokeTheUnderlyingCapabilityExactlyOnce_WhenTheSameIdempotencyKeyResolvesAcrossLoopIterations()
    {
        var (execution, version, toolCatalog) = SetUpMutatingLoopWorkflow(idempotencyKeyExpression: "\"fixed-key\"");
        var executor = new CountingExecutor(WorkflowNodeType.NativeTool);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor]), toolCatalog).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(1); // The external side effect happened exactly once.

        var loopBodyNodeId = version.Nodes.Single(n => n.NodeKey == "loopBody").Id;
        var loopBodyRows = execution.Nodes.Where(n => n.WorkflowNodeId == loopBodyNodeId).ToList();
        loopBodyRows.Should().HaveCount(2); // Dispatched twice (bounded at 2 iterations)...
        loopBodyRows.Should().OnlyContain(n => n.Status == WorkflowExecutionNodeStatus.Completed); // ...but both rows completed.
        loopBodyRows.Should().OnlyContain(n => n.ResolvedIdempotencyKey == "fixed-key");
    }

    [Fact]
    public async Task RunAsync_ShouldInvokeTheUnderlyingCapabilityOnEveryIteration_WhenNoIdempotencyKeyIsConfigured()
    {
        // No IdempotencyKeyExpression at all — the node is still mutating, so the earlier
        // WorkflowRetryPolicyTests coverage already proves a *retry* is capped at one attempt; this
        // proves a *fresh dispatch* (a new loop iteration, not a retry of the same attempt) is a
        // distinct concern and is not itself blocked by the mutating-without-a-key rule.
        var (execution, _, toolCatalog) = SetUpMutatingLoopWorkflow(idempotencyKeyExpression: null);
        var executor = new CountingExecutor(WorkflowNodeType.NativeTool);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor]), toolCatalog).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(2);
    }
}
