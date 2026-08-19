using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class WorkflowExecutionOrchestratorTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new AskLucy.Application.Options.WorkflowRuntimeOptions()));
    private readonly WorkflowPolicyEvaluator _policyEvaluator = new(Substitute.For<IWorkflowPolicyRepository>());
    private readonly AgentToolCatalog _toolCatalog = new([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string configurationJson = "{}") => new(
        key, type, key, null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) =>
        new(_executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard, _policyEvaluator, _toolCatalog, WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    private static WorkflowNodeExecutorRegistry RegistryWithTransform(IWorkflowExpressionEvaluator evaluator) =>
        new([new TransformNodeExecutor(evaluator)]);

    /// <summary>Start → Transform (echoes the input) → End (maps the transform's output to the workflow's declared output).</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpLinearWorkflow(string transformExpression = "{{workflow.text}}")
    {
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("transform", WorkflowNodeType.Transform, $"{{\"expression\":\"{transformExpression}\",\"outputField\":\"result\"}}"),
                Node("end", WorkflowNodeType.End, "{\"outputs\":{\"result\":\"{{steps.transform.result}}\"}}"),
            ],
            [
                new WorkflowConnectionSpec("start", "transform", null, null),
                new WorkflowConnectionSpec("transform", "end", null, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(
            workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{\"text\":\"hello\"}", OwnerId);

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteWithFinalOutput_ForALinearStartTransformEndWorkflow()
    {
        var (execution, _) = SetUpLinearWorkflow();

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.FinalOutputJson.Should().Contain("hello");
        execution.Nodes.Should().HaveCount(3);
        execution.Nodes.Should().OnlyContain(n => n.Status == WorkflowExecutionNodeStatus.Completed);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldResolvePriorStepOutputReferences()
    {
        // Transform's own expression references {{workflow.text}} (its input); End then
        // references {{steps.transform.result}} (a prior step's output) — proves the
        // steps.* namespace is populated from completed node output, not just workflow.*.
        var (execution, version) = SetUpLinearWorkflow();

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        var transformNodeId = version.Nodes.Single(n => n.NodeKey == "transform").Id;
        var transformNode = execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == transformNodeId).Subject;
        transformNode.OutputJson.Should().Be("{\"result\":\"hello\"}");
        execution.FinalOutputJson.Should().Be("{\"result\":\"hello\"}");
    }

    [Fact]
    public async Task RunAsync_ShouldRecordAnErrorAndFail_WhenANodeHasNoRegisteredExecutor()
    {
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("rag", WorkflowNodeType.RagSearch), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "rag", null, null), new WorkflowConnectionSpec("rag", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([])).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle();
        execution.TerminationReason.Should().Contain("rag");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenATransformExpressionReferencesAnUnresolvedValue()
    {
        var (execution, _) = SetUpLinearWorkflow("{{workflow.doesNotExist}}");

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldBeANoOp_WhenTheExecutionIsAlreadyTerminal()
    {
        var (execution, _) = SetUpLinearWorkflow();
        execution.Start();
        execution.Cancel("Already cancelled.");

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, CancellationToken.None);

        await _workflowRepository.DidNotReceive().GetVersionByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldEmitWorkflowStartedAndCompletedEvents()
    {
        var (execution, _) = SetUpLinearWorkflow();

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, CancellationToken.None);

        execution.Events.Should().Contain(e => e.EventType == WorkflowExecutionEventType.WorkflowStarted);
        execution.Events.Should().Contain(e => e.EventType == WorkflowExecutionEventType.WorkflowCompleted);
        execution.Events.Count(e => e.EventType == WorkflowExecutionEventType.NodeCompleted).Should().Be(3);
    }
}
