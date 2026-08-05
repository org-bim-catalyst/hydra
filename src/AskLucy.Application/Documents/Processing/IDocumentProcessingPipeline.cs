namespace AskLucy.Application.Documents.Processing;

/// <summary>
/// Durable, resumable orchestration of a document version through every
/// <see cref="AskLucy.Domain.Documents.DocumentProcessingStageType"/> (FR-020, FR-027, FR-029,
/// FR-030a; research.md Decisions 2, 10). The concrete implementation (added in US2,
/// specs/015-document-intelligence-pipeline tasks.md T073) chains
/// <see cref="IProcessingStageHandler"/> executions via Hangfire, persisting
/// <c>DocumentProcessingJob</c>/<c>DocumentProcessingStage</c> state before/after each stage so a
/// crash mid-pipeline resumes from the first non-completed stage without redoing finished work.
/// </summary>
public interface IDocumentProcessingPipeline
{
    /// <summary>Creates the job/stage rows and schedules processing for a newly uploaded or replaced document version (FR-020).</summary>
    Task EnqueueAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default);

    /// <summary>Re-enqueues a Failed job from its first non-completed stage (FR-029). The caller (command handler) is responsible for rejecting a retry when the job isn't currently Failed.</summary>
    Task RetryAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The actual background execution, scheduled via <c>Hangfire.BackgroundJob.Enqueue&lt;IDocumentProcessingPipeline&gt;</c>
    /// from <see cref="EnqueueAsync"/>/<see cref="RetryAsync"/> — not called directly by any
    /// command handler. Public (part of the interface, not a private method) because Hangfire
    /// must be able to express the job as a method call on a DI-resolvable type.
    /// </summary>
    Task RunJobAsync(Guid documentProcessingJobId, CancellationToken cancellationToken = default);
}
