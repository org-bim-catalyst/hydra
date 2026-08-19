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

/// <summary>T109 — Parallel node concurrency limits and branch isolation (FR-030, research.md Decision 9).</summary>
public sealed class ParallelExecutionTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowPolicyEvaluator _policyEvaluator = new(Substitute.For<IWorkflowPolicyRepository>());
    private readonly AgentToolCatalog _toolCatalog = new([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());

    private sealed class FakeExecutor(WorkflowNodeType nodeType, Func<CancellationToken, Task<WorkflowNodeExecutionResult>> execute) : IWorkflowNodeExecutor
    {
        public WorkflowNodeType NodeType => nodeType;

        public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            execute(cancellationToken);
    }

    private static WorkflowNodeExecutionResult SuccessWith(string field, string value) =>
        WorkflowNodeExecutionResult.Success(JsonSerializer.SerializeToDocument(new Dictionary<string, string> { [field] = value }));

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry, int? maxParallelNodesDefault = null) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator,
        new WorkflowBudgetGuard(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions { DefaultMaxParallelNodes = maxParallelNodesDefault ?? 10 })),
        _policyEvaluator, _toolCatalog,
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _unitOfWork);

    /// <summary>Start → Parallel → [rag, memory, doc] → Merge → End. Branch node types stand in for the real capability nodes — behavior is entirely config/executor-driven, never type-instance-specific.</summary>
    private (WorkflowExecution Execution, WorkflowVersion Version) SetUpParallelWorkflow(string mergeConfigurationJson, string executionPolicyJson = "{}")
    {
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("parallel", WorkflowNodeType.Parallel),
                Node("rag", WorkflowNodeType.RagSearch),
                Node("memory", WorkflowNodeType.MemorySearch),
                Node("doc", WorkflowNodeType.DocumentProcessing),
                Node("merge", WorkflowNodeType.Merge, mergeConfigurationJson),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "parallel", null, null),
                new WorkflowConnectionSpec("parallel", "rag", null, null),
                new WorkflowConnectionSpec("parallel", "memory", null, null),
                new WorkflowConnectionSpec("parallel", "doc", null, null),
                new WorkflowConnectionSpec("rag", "merge", null, null),
                new WorkflowConnectionSpec("memory", "merge", null, null),
                new WorkflowConnectionSpec("doc", "merge", null, null),
                new WorkflowConnectionSpec("merge", "end", null, null),
            ],
            [], "{}", "{}", "{}", executionPolicyJson, "{}", null, OwnerId);

        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        return (execution, version);
    }

    [Fact]
    public async Task RunAsync_ShouldRespectMaxParallelNodes_WhenThePolicyLimitsConcurrency()
    {
        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        async Task<WorkflowNodeExecutionResult> TrackConcurrency(CancellationToken ct)
        {
            lock (gate) { currentConcurrency++; maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentConcurrency); }
            await Task.Delay(30, ct);
            lock (gate) { currentConcurrency--; }
            return SuccessWith("ok", "true");
        }

        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, TrackConcurrency),
            new FakeExecutor(WorkflowNodeType.MemorySearch, TrackConcurrency),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, TrackConcurrency),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory","doc"]}""", """{"maxParallelNodes":1}""");

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        maxObservedConcurrency.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldRunBranchesConcurrently_WhenNotLimited()
    {
        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        async Task<WorkflowNodeExecutionResult> TrackConcurrency(CancellationToken ct)
        {
            lock (gate) { currentConcurrency++; maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentConcurrency); }
            await Task.Delay(30, ct);
            lock (gate) { currentConcurrency--; }
            return SuccessWith("ok", "true");
        }

        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, TrackConcurrency),
            new FakeExecutor(WorkflowNodeType.MemorySearch, TrackConcurrency),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, TrackConcurrency),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory","doc"]}""");

        await CreateOrchestrator(registry, maxParallelNodesDefault: 10).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        maxObservedConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task RunAsync_AllCompleted_ShouldMergeEveryBranchsOutput_AndCompleteTheWorkflow()
    {
        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, _ => Task.FromResult(SuccessWith("value", "rag-value"))),
            new FakeExecutor(WorkflowNodeType.MemorySearch, _ => Task.FromResult(SuccessWith("value", "memory-value"))),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, _ => Task.FromResult(SuccessWith("value", "doc-value"))),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory","doc"]}""");

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.Nodes.Should().Contain(n => n.Status == WorkflowExecutionNodeStatus.Completed && n.OutputJson!.Contains("rag-value"));
        execution.Nodes.Should().Contain(n => n.Status == WorkflowExecutionNodeStatus.Completed && n.OutputJson!.Contains("memory-value"));
        execution.Nodes.Should().Contain(n => n.Status == WorkflowExecutionNodeStatus.Completed && n.OutputJson!.Contains("doc-value"));
    }

    [Fact]
    public async Task RunAsync_AllCompleted_ShouldFailTheWorkflow_WhenAnyBranchFails()
    {
        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, _ => Task.FromResult(SuccessWith("value", "rag-value"))),
            new FakeExecutor(WorkflowNodeType.MemorySearch, _ => Task.FromResult(WorkflowNodeExecutionResult.Failure("memory search failed"))),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, _ => Task.FromResult(SuccessWith("value", "doc-value"))),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory","doc"]}""");

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.Nodes.Should().Contain(n => n.Status == WorkflowExecutionNodeStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_AnyCompleted_ShouldCompleteTheWorkflow_WhenOnlySomeBranchesSucceed()
    {
        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, _ => Task.FromResult(WorkflowNodeExecutionResult.Failure("rag failed"))),
            new FakeExecutor(WorkflowNodeType.MemorySearch, _ => Task.FromResult(SuccessWith("value", "memory-value"))),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, _ => Task.FromResult(WorkflowNodeExecutionResult.Failure("doc failed"))),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"AnyCompleted","branchNodeKeys":["rag","memory","doc"]}""");

        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_FirstCompleted_ShouldNotWaitForASlowerBranch_AndShouldNotIncludeItsOutput()
    {
        var registry = new WorkflowNodeExecutorRegistry([
            new FakeExecutor(WorkflowNodeType.RagSearch, async ct => { await Task.Delay(500, ct); return SuccessWith("value", "slow-rag-value"); }),
            new FakeExecutor(WorkflowNodeType.MemorySearch, _ => Task.FromResult(SuccessWith("value", "fast-memory-value"))),
            new FakeExecutor(WorkflowNodeType.DocumentProcessing, async ct => { await Task.Delay(500, ct); return SuccessWith("value", "slow-doc-value"); }),
            new MergeNodeExecutor(),
        ]);

        var (execution, _) = SetUpParallelWorkflow("""{"strategy":"FirstCompleted","branchNodeKeys":["rag","memory","doc"]}""");

        var started = DateTime.UtcNow;
        await CreateOrchestrator(registry).RunAsync(execution.Id, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(450));
        execution.FinalOutputJson.Should().NotBeNull();
        execution.Nodes.Should().NotContain(n => n.OutputJson != null && n.OutputJson.Contains("slow-"));
    }
}
