using AskLucy.Application.Abstractions;
using Hangfire;

namespace AskLucy.Application.Agents.Runtime;

/// <summary>
/// <see cref="IAgentExecutionRunner"/> implementation (research.md Decision 8). Schedules via the
/// injected <see cref="IBackgroundJobClient"/> rather than the static <c>Hangfire.BackgroundJob</c>
/// facade, mirroring <c>DocumentProcessingPipeline</c> — the static facade requires a live
/// <c>JobStorage.Current</c>, which makes <see cref="EnqueueAsync"/> impossible to unit test
/// without one.
/// </summary>
public sealed class AgentExecutionRunner(
    AgentExecutionOrchestrator orchestrator, IBackgroundJobClient backgroundJobClient) : IAgentExecutionRunner
{
    public Task EnqueueAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        // Hangfire's serializer captures the expression's arguments at schedule time, not by
        // reference — CancellationToken.None here is intentional: Hangfire supplies its own
        // shutdown-aware token to the running job, this parameter only exists to satisfy the
        // interface signature the expression tree captures (mirrors DocumentProcessingPipeline).
        backgroundJobClient.Enqueue<IAgentExecutionRunner>(r => r.RunJobAsync(executionId, CancellationToken.None));
        return Task.CompletedTask;
    }

    public Task RunJobAsync(Guid executionId, CancellationToken cancellationToken = default) =>
        orchestrator.RunAsync(executionId, cancellationToken);
}
