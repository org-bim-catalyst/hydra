namespace AskLucy.Application.Abstractions;

/// <summary>
/// Schedules and runs a <see cref="Domain.Workflows.WorkflowExecution"/> in the background
/// (spec.md FR-047, research.md Decision 7). Mirrors <c>IAgentExecutionRunner</c>'s shape exactly
/// — <see cref="EnqueueAsync"/> is called by a command handler; <see cref="RunJobAsync"/> is the
/// actual background execution, scheduled via <c>Hangfire.IBackgroundJobClient.Enqueue&lt;
/// IWorkflowExecutionRunner&gt;</c> from <see cref="EnqueueAsync"/>, never invoked directly.
/// </summary>
public interface IWorkflowExecutionRunner
{
    Task EnqueueAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task RunJobAsync(Guid executionId, CancellationToken cancellationToken = default);
}
