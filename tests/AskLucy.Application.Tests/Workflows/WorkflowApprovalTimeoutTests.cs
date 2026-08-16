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

/// <summary>FR-037 — a Human Approval node's configured <see cref="WorkflowNode.TimeoutSeconds"/> applies its failure policy once elapsed; an unset timeout waits indefinitely. Timeout is only checked when <see cref="WorkflowExecutionOrchestrator.RunAsync"/> is re-invoked while a decision is still pending (see the orchestrator's class doc comment) — these tests simulate elapsed time by backdating <see cref="WorkflowApproval.CreatedAtUtc"/> directly, since there is no recurring trigger yet to age it naturally.</summary>
public sealed class WorkflowApprovalTimeoutTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));

    public WorkflowApprovalTimeoutTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator() => new(
        _executionRepository, _workflowRepository, new WorkflowNodeExecutorRegistry([]), _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), new AgentToolCatalog([], EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    /// <summary>Start → HumanApproval (with the given timeout) → End.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpWorkflow(int? timeoutSeconds)
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, int? nodeTimeoutSeconds = null) =>
            new(key, type, key, null, "{}", "{}", "{}", "[]", nodeTimeoutSeconds, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var workflow = Workflow.Create(OwnerId, "Timeout Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("approval", WorkflowNodeType.HumanApproval, timeoutSeconds), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "approval", null, null), new WorkflowConnectionSpec("approval", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldFailTheExecution_WhenTheConfiguredTimeoutHasElapsedWhileWaitingForApproval()
    {
        var (execution, _) = SetUpWorkflow(timeoutSeconds: 60);
        var orchestrator = CreateOrchestrator();
        await orchestrator.RunAsync(execution.Id, CancellationToken.None);
        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);

        var approval = execution.Approvals.Single();
        approval.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-120);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        approval.Decision.Should().Be(WorkflowApprovalDecision.Cancel);
        execution.Nodes.Should().ContainSingle(n => n.Status == WorkflowExecutionNodeStatus.Failed);
        execution.Errors.Should().ContainSingle(e => e.Category == WorkflowErrorCategory.Timeout);
    }

    [Fact]
    public async Task RunAsync_ShouldStillBeWaiting_WhenTheConfiguredTimeoutHasNotYetElapsed()
    {
        var (execution, _) = SetUpWorkflow(timeoutSeconds: 3600);
        var orchestrator = CreateOrchestrator();
        await orchestrator.RunAsync(execution.Id, CancellationToken.None);
        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);

        var approval = execution.Approvals.Single();
        approval.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-10);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        approval.Decision.Should().Be(WorkflowApprovalDecision.Pending);
    }

    [Fact]
    public async Task RunAsync_ShouldWaitIndefinitely_WhenNoTimeoutIsConfigured()
    {
        var (execution, _) = SetUpWorkflow(timeoutSeconds: null);
        var orchestrator = CreateOrchestrator();
        await orchestrator.RunAsync(execution.Id, CancellationToken.None);
        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);

        var approval = execution.Approvals.Single();
        approval.CreatedAtUtc = DateTime.UtcNow.AddDays(-30);

        await orchestrator.RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.WaitingForApproval);
        approval.Decision.Should().Be(WorkflowApprovalDecision.Pending);
    }
}
