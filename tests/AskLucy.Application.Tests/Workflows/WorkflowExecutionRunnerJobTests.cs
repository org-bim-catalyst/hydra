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
/// spec.md User Story 6 (FR-048, SC-007: ≤5s) — the orchestrator's node-boundary loop observes an
/// externally-requested pause/cancel (<c>PauseWorkflowExecutionCommand</c>/<c>CancelWorkflowExecutionCommand</c>,
/// each running in a separate HTTP request against its own tracked <c>WorkflowExecution</c>) and
/// exits immediately, never touching (or re-saving) this run's own now-stale in-memory execution.
/// </summary>
public sealed class WorkflowExecutionRunnerJobTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator,
        new WorkflowBudgetGuard(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions())),
        new WorkflowPolicyEvaluator(_policyRepository), new AgentToolCatalog([], EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string configurationJson = "{}") => new(
        key, type, key, null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    /// <summary>Start → Transform1 → Transform2 → End — two Transform nodes so a pause/cancel observed between them proves only the first ran.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpTwoStepWorkflow()
    {
        var workflow = Workflow.Create(OwnerId, "Two-Step Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("transform1", WorkflowNodeType.Transform, """{"expression":"1","outputField":"result"}"""),
                Node("transform2", WorkflowNodeType.Transform, """{"expression":"2","outputField":"result"}"""),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "transform1", null, null),
                new WorkflowConnectionSpec("transform1", "transform2", null, null),
                new WorkflowConnectionSpec("transform2", "end", null, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldExitAtTheNextNodeBoundary_WhenAConcurrentPauseIsObserved()
    {
        var (execution, _) = SetUpTwoStepWorkflow();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>())
            .Returns(WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Paused);

        var registry = new WorkflowNodeExecutorRegistry([new TransformNodeExecutor(_expressionEvaluator)]);
        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Nodes.Should().HaveCount(2); // Start + transform1 only — transform2/end never ran.
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public async Task RunAsync_ShouldExitAtTheNextNodeBoundary_WhenAConcurrentCancelIsObserved()
    {
        var (execution, _) = SetUpTwoStepWorkflow();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>())
            .Returns(WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Cancelled);

        var registry = new WorkflowNodeExecutorRegistry([new TransformNodeExecutor(_expressionEvaluator)]);
        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Nodes.Should().HaveCount(2);
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteNormally_WhenNoExternalPauseOrCancelIsObserved()
    {
        var (execution, _) = SetUpTwoStepWorkflow();
        _executionRepository.GetStatusAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(WorkflowExecutionStatus.Running);

        var registry = new WorkflowNodeExecutorRegistry([new TransformNodeExecutor(_expressionEvaluator)]);
        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Nodes.Should().HaveCount(4);
        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
    }
}
