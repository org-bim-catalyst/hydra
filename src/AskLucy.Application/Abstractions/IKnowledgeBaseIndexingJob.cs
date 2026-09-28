namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background indexing of one <c>KnowledgeBaseDocument</c> (specs/067-notifications-communication-hub
/// T237; specs/016 <c>IIndexingOrchestrator</c> had no production caller before this). The concrete
/// implementation, <c>KnowledgeBaseIndexingJob</c>, lives in <c>AskLucy.Application</c> — not
/// <c>Infrastructure</c> — for the same reason <c>IMemoryExtractionJob</c>/<c>MemoryExtractionJob</c>
/// does: it is pure orchestration over Application abstractions (the indexing orchestrator, the job
/// repository, the notification publisher), no framework-specific code. Enqueued via
/// <c>IBackgroundJobClient</c> against this interface, never the concrete type, so Hangfire resolves
/// it through the container.
/// </summary>
public interface IKnowledgeBaseIndexingJob
{
    /// <summary>
    /// Runs the <see cref="AskLucy.Domain.Retrieval.IndexingJob"/> identified by
    /// <paramref name="indexingJobId"/>: starts it, calls the indexing orchestrator, settles the
    /// job (<c>Complete</c>/<c>Fail</c>), and — when it is the last job in progress for its
    /// knowledge base — settles the knowledge base's own index status too. Idempotent against a
    /// Hangfire retry of the same job id: a job already <c>Completed</c>/<c>Failed</c> is a no-op.
    /// Automatically retried by Hangfire's <c>[AutomaticRetry(Attempts = 3)]</c> on the
    /// implementation; the final attempt's failure is the one that settles the job.
    /// </summary>
    Task RunAsync(Guid indexingJobId, CancellationToken cancellationToken = default);
}
