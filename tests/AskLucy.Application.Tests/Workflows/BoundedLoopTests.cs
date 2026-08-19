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
/// T111 — a loop-back connection (<see cref="WorkflowConnection.LoopBackBranchLabel"/>) always
/// stops at its maximum iteration count, regardless of whether anything internal to the loop body
/// would ever decide to exit on its own (FR-032) — the fake executor here always "succeeds"
/// (never signals a reason to stop), so the iteration cap is the *only* thing that can end the run.
/// </summary>
public sealed class BoundedLoopTests
{
    private const string OwnerId = "user-1";

    private sealed class CountingExecutor(WorkflowNodeType nodeType) : IWorkflowNodeExecutor
    {
        public int InvocationCount { get; private set; }

        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new { iteration = InvocationCount })));
        }
    }

    /// <summary>Start → loopBody → [loop-back to loopBody / forward to end].</summary>
    private static (WorkflowExecution Execution, WorkflowVersion Version) SetUpLoopWorkflow(string loopBodyConfigurationJson, string executionPolicyJson = "{}")
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var workflow = Workflow.Create(OwnerId, "Loop Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("loopBody", WorkflowNodeType.RagSearch, loopBodyConfigurationJson),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "loopBody", null, null),
                new WorkflowConnectionSpec("loopBody", "loopBody", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowConnectionSpec("loopBody", "end", null, null),
            ],
            [], "{}", "{}", "{}", executionPolicyJson, "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        return (execution, version);
    }

    private static WorkflowExecutionOrchestrator CreateOrchestrator(
        CountingExecutor loopBodyExecutor, IWorkflowExecutionRepository executionRepository, IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork)
    {
        var expressionEvaluator = new WorkflowExpressionEvaluator();
        var registry = new WorkflowNodeExecutorRegistry([loopBodyExecutor]);
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
    public async Task RunAsync_ShouldStopTheLoop_AtExactlyTheNodeDeclaredMaxIterations()
    {
        var (execution, version) = SetUpLoopWorkflow("""{"maxIterations":3}""");
        var executor = new CountingExecutor(WorkflowNodeType.RagSearch);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateOrchestrator(executor, executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(3);
        var loopBodyNodeId = version.Nodes.Single(n => n.NodeKey == "loopBody").Id;
        execution.Nodes.Count(n => n.WorkflowNodeId == loopBodyNodeId).Should().Be(3);
        execution.Nodes.Where(n => n.WorkflowNodeId == loopBodyNodeId).Should().OnlyContain(n => n.Status == WorkflowExecutionNodeStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_ShouldCapIterations_AtThePolicyCeiling_EvenWhenTheNodeDeclaresAMuchHigherMax()
    {
        var (execution, version) = SetUpLoopWorkflow("""{"maxIterations":1000}""", """{"maxLoopIterations":2}""");
        var executor = new CountingExecutor(WorkflowNodeType.RagSearch);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateOrchestrator(executor, executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        executor.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldReachTheExitPath_AfterTheLoopStops()
    {
        var (execution, version) = SetUpLoopWorkflow("""{"maxIterations":2}""");
        var executor = new CountingExecutor(WorkflowNodeType.RagSearch);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateOrchestrator(executor, executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        var endNodeId = version.Nodes.Single(n => n.NodeKey == "end").Id;
        execution.Nodes.Should().ContainSingle(n => n.WorkflowNodeId == endNodeId).Which.Status.Should().Be(WorkflowExecutionNodeStatus.Completed);
        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenTheLoopHasNoExitConnectionOnceTheCapIsReached()
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var workflow = Workflow.Create(OwnerId, "Loop Without Exit", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("loopBody", WorkflowNodeType.RagSearch, """{"maxIterations":2}""")],
            [
                new WorkflowConnectionSpec("start", "loopBody", null, null),
                new WorkflowConnectionSpec("loopBody", "loopBody", WorkflowConnection.LoopBackBranchLabel, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        var executor = new CountingExecutor(WorkflowNodeType.RagSearch);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        await CreateOrchestrator(executor, executionRepository, workflowRepository, unitOfWork).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        executor.InvocationCount.Should().Be(2);
    }
}
