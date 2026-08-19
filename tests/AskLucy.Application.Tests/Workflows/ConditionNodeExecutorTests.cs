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

/// <summary>T108 — <see cref="ConditionNodeExecutor"/> itself only produces the boolean result; branch routing/skip-recording is <see cref="WorkflowExecutionOrchestrator"/>'s job, covered by the orchestrator-level tests below.</summary>
public sealed class ConditionNodeExecutorTests
{
    private const string OwnerId = "user-1";
    private readonly ConditionNodeExecutor _executor = new(new WorkflowExpressionEvaluator());

    private static WorkflowNode BuildNode(string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "node", WorkflowNodeType.Condition, "node", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResultTrue_WhenTheExpressionEvaluatesToTrue()
    {
        var node = BuildNode("""{"expression":"{{workflow.category}} == \"urgent\""}""");
        using var input = JsonDocument.Parse("""{"workflow.category":"urgent"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResultFalse_WhenTheExpressionEvaluatesToFalse()
    {
        var node = BuildNode("""{"expression":"{{workflow.category}} == \"urgent\""}""");
        using var input = JsonDocument.Parse("""{"workflow.category":"routine"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("result").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheExpressionDoesNotEvaluateToABoolean()
    {
        var node = BuildNode("""{"expression":"{{workflow.category}}"}""");
        using var input = JsonDocument.Parse("""{"workflow.category":"urgent"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("boolean");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheExpressionFieldIsMissing()
    {
        var node = BuildNode("{}");
        using var input = JsonDocument.Parse("{}");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("expression");
    }

    // Orchestrator-level branch routing / skip-recording (FR-029) — Start → Condition → [true:
    // transformTrue / false: transformFalse] → End, both branches converging on the same End node.

    private static (WorkflowExecution Execution, WorkflowVersion Version) SetUpBranchingWorkflow(string category)
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var workflow = Workflow.Create(OwnerId, "Branching Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("condition", WorkflowNodeType.Condition, """{"expression":"{{workflow.category}} == \"urgent\""}"""),
                Node("transformTrue", WorkflowNodeType.Transform, """{"expression":"\"true-path\"","outputField":"branch"}"""),
                Node("transformFalse", WorkflowNodeType.Transform, """{"expression":"\"false-path\"","outputField":"branch"}"""),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "condition", null, null),
                new WorkflowConnectionSpec("condition", "transformTrue", "true", null),
                new WorkflowConnectionSpec("condition", "transformFalse", "false", null),
                new WorkflowConnectionSpec("transformTrue", "end", null, null),
                new WorkflowConnectionSpec("transformFalse", "end", null, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, $$"""{"category":"{{category}}"}""", OwnerId);
        return (execution, version);
    }

    private static WorkflowExecutionOrchestrator CreateBranchingOrchestrator(
        IWorkflowExecutionRepository executionRepository, IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork)
    {
        var expressionEvaluator = new WorkflowExpressionEvaluator();
        var registry = new WorkflowNodeExecutorRegistry([new ConditionNodeExecutor(expressionEvaluator), new TransformNodeExecutor(expressionEvaluator)]);
        return new WorkflowExecutionOrchestrator(
            executionRepository, workflowRepository, registry, expressionEvaluator,
            new WorkflowBudgetGuard(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions())),
            new WorkflowPolicyEvaluator(Substitute.For<IWorkflowPolicyRepository>()),
            new AgentToolCatalog([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry()),
            WorkflowOrchestratorTestHelpers.NoOpNotifier(),
            WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(),
            unitOfWork);
    }

    [Fact]
    public async Task RunAsync_ShouldRouteToTheTrueBranch_AndMarkTheFalseBranchSkipped()
    {
        var (execution, version) = SetUpBranchingWorkflow("urgent");
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateBranchingOrchestrator(executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        var trueNodeId = version.Nodes.Single(n => n.NodeKey == "transformTrue").Id;
        var falseNodeId = version.Nodes.Single(n => n.NodeKey == "transformFalse").Id;
        execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == trueNodeId).Which.Status.Should().Be(WorkflowExecutionNodeStatus.Completed);
        execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == falseNodeId).Which.Status.Should().Be(WorkflowExecutionNodeStatus.Skipped);
    }

    [Fact]
    public async Task RunAsync_ShouldRouteToTheFalseBranch_AndMarkTheTrueBranchSkipped()
    {
        var (execution, version) = SetUpBranchingWorkflow("routine");
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateBranchingOrchestrator(executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        var trueNodeId = version.Nodes.Single(n => n.NodeKey == "transformTrue").Id;
        var falseNodeId = version.Nodes.Single(n => n.NodeKey == "transformFalse").Id;
        execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == falseNodeId).Which.Status.Should().Be(WorkflowExecutionNodeStatus.Completed);
        execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == trueNodeId).Which.Status.Should().Be(WorkflowExecutionNodeStatus.Skipped);
    }
}
