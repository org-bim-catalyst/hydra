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

/// <summary>spec.md User Story 7 (FR-040) — a node's own <see cref="WorkflowNode.RetryPolicyJson"/> governs per-attempt backoff up to its configured maximum attempts; a mutating node with no idempotency key is never retried more than once, regardless of policy.</summary>
public sealed class WorkflowRetryPolicyTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()));

    public WorkflowRetryPolicyTests()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
    }

    private sealed class FlakyExecutor(WorkflowNodeType nodeType, int failuresBeforeSuccess) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(InvocationCount <= failuresBeforeSuccess
                ? WorkflowNodeExecutionResult.Failure("Simulated transient failure.")
                : WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { ok = true })));
        }
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry, AgentToolCatalog? toolCatalog = null) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard,
        new WorkflowPolicyEvaluator(_policyRepository), toolCatalog ?? new AgentToolCatalog([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry()),
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string configurationJson = "{}", string? retryPolicyJson = null, string? idempotencyKeyExpression = null) => new(
        key, type, key, null, "{}", "{}", configurationJson, "[]", null, retryPolicyJson, WorkflowNodeApprovalPolicy.NeverRequire, idempotencyKeyExpression, null, 0, 0);

    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpWorkflow(WorkflowNodeSpec middleNode, string errorPolicyJson = "{}")
    {
        var workflow = Workflow.Create(OwnerId, "Retry Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), middleNode, Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", middleNode.NodeKey, null, null), new WorkflowConnectionSpec(middleNode.NodeKey, "end", null, null)],
            [], "{}", "{}", errorPolicyJson, "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldRetryUpToMaxAttempts_ThenSucceed()
    {
        var middleNode = Node("flaky", WorkflowNodeType.Transform, retryPolicyJson: """{"maxAttempts":3,"initialDelaySeconds":0}""");
        var (execution, version) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.Transform, failuresBeforeSuccess: 2);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(3);
        var flakyNodeId = version.Nodes.Single(n => n.NodeKey == "flaky").Id;
        execution.Nodes.Single(n => n.WorkflowNodeId == flakyNodeId).RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldFailTheExecution_WhenMaxAttemptsAreExhausted()
    {
        var middleNode = Node("alwaysFails", WorkflowNodeType.Transform, retryPolicyJson: """{"maxAttempts":2,"initialDelaySeconds":0}""");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.Transform, failuresBeforeSuccess: int.MaxValue);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        executor.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldNotRetry_WhenNoRetryPolicyIsConfigured()
    {
        var middleNode = Node("noPolicy", WorkflowNodeType.Transform);
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.Transform, failuresBeforeSuccess: 1);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        executor.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldWaitBetweenAttempts_AccordingToTheConfiguredInitialDelay()
    {
        var middleNode = Node("slow-retry", WorkflowNodeType.Transform, retryPolicyJson: """{"maxAttempts":2,"initialDelaySeconds":1}""");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.Transform, failuresBeforeSuccess: 1);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor])).RunAsync(execution.Id, CancellationToken.None);
        stopwatch.Stop();

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9));
    }

    [Fact]
    public async Task RunAsync_ShouldNeverRetryMoreThanOnce_ForAMutatingNodeWithNoIdempotencyKey()
    {
        var mutatingTool = Substitute.For<IAgentTool>();
        mutatingTool.Name.Returns("FakeMutatingTool");
        mutatingTool.RiskLevel.Returns(AgentToolRiskLevel.Low);
        mutatingTool.RequiredPermissions.Returns([AgentToolPermission.ModifyData]);
        var toolCatalog = new AgentToolCatalog([mutatingTool], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());

        var middleNode = Node(
            "mutating", WorkflowNodeType.NativeTool, """{"toolName":"FakeMutatingTool"}""",
            retryPolicyJson: """{"maxAttempts":5,"initialDelaySeconds":0}""");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.NativeTool, failuresBeforeSuccess: int.MaxValue);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor]), toolCatalog).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        executor.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldRetryAMutatingNode_WhenAnIdempotencyKeyIsConfigured()
    {
        var mutatingTool = Substitute.For<IAgentTool>();
        mutatingTool.Name.Returns("FakeMutatingTool");
        mutatingTool.RiskLevel.Returns(AgentToolRiskLevel.Low);
        mutatingTool.RequiredPermissions.Returns([AgentToolPermission.ModifyData]);
        var toolCatalog = new AgentToolCatalog([mutatingTool], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());

        var middleNode = Node(
            "mutatingWithKey", WorkflowNodeType.NativeTool, """{"toolName":"FakeMutatingTool"}""",
            retryPolicyJson: """{"maxAttempts":3,"initialDelaySeconds":0}""", idempotencyKeyExpression: "\"fixed-key\"");
        var (execution, _) = SetUpWorkflow(middleNode);
        var executor = new FlakyExecutor(WorkflowNodeType.NativeTool, failuresBeforeSuccess: 1);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([executor]), toolCatalog).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(2);
    }
}
