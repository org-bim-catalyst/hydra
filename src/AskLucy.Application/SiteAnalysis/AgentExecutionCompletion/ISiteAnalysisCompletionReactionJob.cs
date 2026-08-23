namespace AskLucy.Application.SiteAnalysis.AgentExecutionCompletion;

/// <summary>
/// Reacts to a completed site-analysis <c>AgentExecution</c> (research.md Decision 1 addendum,
/// ADR-driven: <c>IAgentExecutionRunner.EnqueueAsync</c> now returns the Hangfire job id so this
/// can be scheduled as a <c>BackgroundJob.ContinueJobWith</c> off it \u2014 zero changes to
/// <c>AgentExecutionOrchestrator</c> itself). Public interface method because Hangfire must be
/// able to express the continuation as a method call on a DI-resolvable type, mirroring
/// <see cref="Abstractions.IAgentExecutionRunner"/>'s own shape.
/// </summary>
public interface ISiteAnalysisCompletionReactionJob
{
    Task ProcessAsync(Guid agentExecutionId, CancellationToken cancellationToken = default);
}
