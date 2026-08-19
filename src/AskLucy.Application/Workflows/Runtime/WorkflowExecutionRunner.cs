using AskLucy.Application.Abstractions;
using Hangfire;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// <see cref="IWorkflowExecutionRunner"/> implementation (research.md Decision 7) — lives in
/// Application, not Infrastructure, mirroring <c>AgentExecutionRunner</c>'s precedent exactly
/// (Hangfire's <see cref="IBackgroundJobClient"/> is already referenced directly from Application
/// elsewhere; the Hangfire entry point for a multi-node orchestration is itself Application-layer
/// logic, not an Infrastructure concern). Schedules via the injected <see cref="IBackgroundJobClient"/>
/// rather than the static <c>Hangfire.BackgroundJob</c> facade, so <see cref="EnqueueAsync"/> stays
/// unit-testable without a live <c>JobStorage.Current</c>.
/// </summary>
public sealed class WorkflowExecutionRunner(
    WorkflowExecutionOrchestrator orchestrator, IBackgroundJobClient backgroundJobClient) : IWorkflowExecutionRunner
{
    public Task EnqueueAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        // CancellationToken.None is intentional — Hangfire supplies its own shutdown-aware token
        // to the running job; this parameter only satisfies the interface signature the
        // expression tree captures at schedule time (mirrors AgentExecutionRunner).
        backgroundJobClient.Enqueue<IWorkflowExecutionRunner>(r => r.RunJobAsync(executionId, CancellationToken.None));
        return Task.CompletedTask;
    }

    public Task RunJobAsync(Guid executionId, CancellationToken cancellationToken = default) =>
        orchestrator.RunAsync(executionId, cancellationToken);
}
