namespace AskLucy.Application.Abstractions;

/// <summary>
/// Schedules and runs an <see cref="Domain.Agents.AgentExecution"/> in the background (spec.md
/// FR-017, research.md Decision 8). Mirrors <c>IDocumentProcessingPipeline</c>'s shape exactly —
/// <see cref="EnqueueAsync"/> is called by a command handler; <see cref="RunJobAsync"/> is the
/// actual background execution, scheduled via <c>Hangfire.IBackgroundJobClient.Enqueue&lt;
/// IAgentExecutionRunner&gt;</c> from <see cref="EnqueueAsync"/>, never invoked directly. Public
/// (part of the interface) because Hangfire must be able to express the job as a method call on a
/// DI-resolvable type.
/// </summary>
public interface IAgentExecutionRunner
{
    Task EnqueueAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task RunJobAsync(Guid executionId, CancellationToken cancellationToken = default);
}
