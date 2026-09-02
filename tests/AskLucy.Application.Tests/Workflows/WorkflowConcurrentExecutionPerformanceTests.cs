using System.Collections.Concurrent;
using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md "Concurrency"/"Performance" sections (Polish phase T202) — 50 simultaneous executions
/// across 10 users complete with no conflicting-write corruption and no deadlock. Backed by a
/// hand-written, genuinely thread-safe <see cref="InMemoryWorkflowExecutionRepository"/> rather
/// than NSubstitute mocks for the one dependency actually mutated under concurrent load — NSubstitute's
/// own call-recording internals are not designed for concurrent invocation from many threads at
/// once, which would make a "no corruption" assertion meaningless (a flaky mock, not a proven
/// guarantee). Every other dependency here is genuinely read-only or a no-op, so NSubstitute is
/// fine for those (never asserted on, never mutated after setup).
/// </summary>
public sealed class WorkflowConcurrentExecutionPerformanceTests
{
    private const int UserCount = 10;
    private const int ExecutionsPerUser = 5; // 10 * 5 = 50 total.

    /// <summary>Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> — the only repository this test's 50 parallel <c>RunAsync</c> calls genuinely mutate concurrently.</summary>
    private sealed class InMemoryWorkflowExecutionRepository : IWorkflowExecutionRepository
    {
        private readonly ConcurrentDictionary<Guid, WorkflowExecution> _executions = new();

        public Task<WorkflowExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_executions.GetValueOrDefault(id));

        public Task<WorkflowExecution?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by WorkflowExecutionOrchestrator.RunAsync.");

        public Task<WorkflowExecutionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_executions.TryGetValue(id, out var execution) ? execution.Status : (WorkflowExecutionStatus?)null);

        public void Add(WorkflowExecution execution) => _executions[execution.Id] = execution;

        public Task<(IReadOnlyList<WorkflowExecution> Items, string? NextCursor)> ListByUserAsync(
            string userId, Guid? workflowId, WorkflowExecutionStatus? status, WorkflowExecutionTriggerType? triggerType,
            string? cursor, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by WorkflowExecutionOrchestrator.RunAsync.");

        public Task<int> CountActiveByUserAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by WorkflowExecutionOrchestrator.RunAsync.");

        public Task<WorkflowStatisticsDto> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by WorkflowExecutionOrchestrator.RunAsync.");
    }

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
        new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    [Fact]
    public async Task RunAsync_ShouldCompleteFiftySimultaneousExecutionsAcrossTenUsers_WithNoCrossExecutionCorruption()
    {
        var workflow = Workflow.Create("template-owner", "Perf Workflow", null, WorkflowType.Manual, "template-owner");
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("transform", WorkflowNodeType.Transform, """{"expression":"1","outputField":"result"}"""),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowConnectionSpec("start", "transform", null, null),
                new WorkflowConnectionSpec("transform", "end", null, null),
            ],
            [], "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}", null, "template-owner");

        var executionRepository = new InMemoryWorkflowExecutionRepository();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        policyRepository.ListEnabledForNodeAsync(Arg.Any<WorkflowNodeType>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowPolicy>)[]);
        var registry = new WorkflowNodeExecutorRegistry([new TransformNodeExecutor(new WorkflowExpressionEvaluator())]);

        var executions = new List<WorkflowExecution>();
        for (var userIndex = 0; userIndex < UserCount; userIndex++)
        {
            var userId = $"user-{userIndex}";
            for (var i = 0; i < ExecutionsPerUser; i++)
            {
                var execution = WorkflowExecution.Create(workflow.Id, version.Id, userId, WorkflowExecutionTriggerType.Manual, null, "{}", userId);
                executionRepository.Add(execution);
                executions.Add(execution);
            }
        }

        executions.Should().HaveCount(UserCount * ExecutionsPerUser);

        Task RunOne(WorkflowExecution execution)
        {
            // Mirrors production: every execution gets its own orchestrator instance (a fresh
            // scope per background job) — only the repository/notifier/audit-log dependencies are
            // actually shared across the 50 concurrent runs, exactly as they would be for a
            // singleton-scoped SignalR notifier or a shared read model.
            var orchestrator = new WorkflowExecutionOrchestrator(
                executionRepository, workflowRepository, registry, new WorkflowExpressionEvaluator(),
                new WorkflowBudgetGuard(Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions())),
                new WorkflowPolicyEvaluator(policyRepository), new AgentToolCatalog([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry()),
                WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), Substitute.For<IUnitOfWork>());
            return orchestrator.RunAsync(execution.Id, CancellationToken.None);
        }

        var stopwatch = Stopwatch.StartNew();
        // WaitAsync is the deadlock guard — a hang here fails the test instead of hanging the suite.
        await Task.WhenAll(executions.Select(RunOne)).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        stopwatch.Stop();

        executions.Should().OnlyContain(e => e.Status == WorkflowExecutionStatus.Completed);
        // No cross-execution bleed: every execution dispatched exactly its own 3 nodes, never another execution's.
        executions.Should().OnlyContain(e => e.Nodes.Count == 3);
        executions.Select(e => e.Id).Distinct().Should().HaveCount(UserCount * ExecutionsPerUser);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }
}
