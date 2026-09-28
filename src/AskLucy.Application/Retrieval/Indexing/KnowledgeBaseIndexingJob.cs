using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Domain.Retrieval;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Retrieval.Indexing;

/// <summary>
/// Implements <see cref="IKnowledgeBaseIndexingJob"/> (specs/067-notifications-communication-hub
/// T235/T237). Each stage (start, settle) is its own <see cref="IUnitOfWork.SaveChangesAsync"/>
/// call, mirroring <c>DocumentProcessingPipeline</c>'s progressive-persistence precedent for
/// long-running background jobs, so a crash mid-run resumes from durable state rather than
/// redoing completed work.
///
/// <para>Whether the knowledge base itself settles (<c>MarkIndexed</c>/<c>MarkIndexFailed</c>) is
/// decided from this job's own outcome once no sibling job is left in progress — a documented
/// simplification of tasks.md T235's "partial when any job since the last settle failed": there is
/// no repository query for a knowledge base's full job history, only
/// <see cref="IIndexingJobRepository.HasJobInProgressAsync"/>, so an earlier sibling job's failure
/// that already settled is not retroactively reconsidered here.</para>
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed class KnowledgeBaseIndexingJob(
    IIndexingJobRepository indexingJobRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseDocumentRepository knowledgeBaseDocumentRepository,
    IIndexingOrchestrator indexingOrchestrator,
    IRetrievalIndexingNotifier retrievalIndexingNotifier,
    INotificationPublisher notificationPublisher,
    IOperationalFailureRecorder operationalFailureRecorder,
    IUnitOfWork unitOfWork,
    ILogger<KnowledgeBaseIndexingJob> logger) : IKnowledgeBaseIndexingJob
{
    private const string SystemActor = "system:retrieval-indexing";

    public async Task RunAsync(Guid indexingJobId, CancellationToken cancellationToken = default)
    {
        var job = await indexingJobRepository.GetByIdAsync(indexingJobId, cancellationToken);
        if (job is null)
        {
            KnowledgeBaseIndexingJobLog.JobNotFound(logger, indexingJobId);
            return;
        }

        // A Hangfire retry of the same job id after it already settled — nothing left to do, and
        // re-running would publish document/knowledge-base notifications a second time.
        if (job.Status is IndexingJobStatus.Completed or IndexingJobStatus.Failed)
        {
            return;
        }

        var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(job.KnowledgeBaseId, cancellationToken);
        if (knowledgeBase is null)
        {
            KnowledgeBaseIndexingJobLog.KnowledgeBaseNotFound(logger, job.KnowledgeBaseId);
            return;
        }

        var knowledgeBaseDocument = job.KnowledgeBaseDocumentId is { } documentId
            ? await knowledgeBaseDocumentRepository.GetByIdAsync(documentId, cancellationToken)
            : null;

        if (knowledgeBaseDocument is null)
        {
            // Deleted between enqueue and run — nothing to index and nothing to tell the owner
            // about; the job simply never happened as far as the user is concerned.
            job.Fail("The document being indexed was removed.", SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        job.Start(SystemActor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await retrievalIndexingNotifier.NotifyStageChangedAsync(knowledgeBase.OwnerId, knowledgeBase.Id, job.Id, "Indexing", "InProgress", cancellationToken);

        string? failureReason = null;
        var succeeded = true;
        var partial = false;

        try
        {
            var outcome = await indexingOrchestrator.IndexKnowledgeBaseDocumentAsync(
                knowledgeBaseDocument.Id, forceFullReindex: job.JobType == IndexingJobType.FullReindex, cancellationToken);

            switch (outcome)
            {
                case IndexingOutcome.Completed:
                    break;
                case IndexingOutcome.PartiallyCompleted:
                    partial = true;
                    break;
                case IndexingOutcome.Failed:
                default:
                    succeeded = false;
                    failureReason = "Indexing could not extract usable content from this document.";
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            succeeded = false;
            failureReason = "An unexpected error interrupted indexing.";
            KnowledgeBaseIndexingJobLog.IndexingFailed(logger, ex, job.Id);
        }

        if (succeeded)
        {
            job.Complete(SystemActor);
        }
        else
        {
            job.Fail(failureReason!, SystemActor);
        }

        // T235 — one document.indexing.completed/.failed per job, before the final save.
        notificationPublisher.Publish(new NotificationRequest(
            succeeded ? NotificationTypeKeys.DocumentIndexingCompleted : NotificationTypeKeys.DocumentIndexingFailed,
            new NotificationRecipient.User(knowledgeBase.OwnerId),
            succeeded
                ? new Dictionary<string, string?> { ["documentName"] = knowledgeBaseDocument.FileName }
                : new Dictionary<string, string?> { ["documentName"] = knowledgeBaseDocument.FileName, ["failureSummary"] = failureReason },
            knowledgeBaseDocument.DocumentId is { } linkedDocumentId ? new RelatedItem("Document", linkedDocumentId.ToString()) : null,
            EventKey: $"indexing-job:{job.Id}:{(succeeded ? "completed" : "failed")}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await retrievalIndexingNotifier.NotifyStageChangedAsync(
            knowledgeBase.OwnerId, knowledgeBase.Id, job.Id, "Indexing", succeeded ? "Completed" : "Failed", cancellationToken);

        if (!succeeded)
        {
            await retrievalIndexingNotifier.NotifyJobFailedAsync(knowledgeBase.OwnerId, knowledgeBase.Id, job.Id, failureReason!, cancellationToken);
            operationalFailureRecorder.Record(new OperationalFailureReport
            {
                Engine = OperationalFailureEngine.DocumentProcessing,
                Operation = "Knowledge-base indexing",
                Kind = OperationalFailureKind.JobFailedAfterRetries,
                Reason = failureReason!,
                Subject = new OperationalFailureSubject("KnowledgeBase", knowledgeBase.Id),
            });
        }

        if (!await indexingJobRepository.HasJobInProgressAsync(knowledgeBase.Id, cancellationToken))
        {
            if (succeeded)
            {
                knowledgeBase.MarkIndexed(partial, SystemActor);
            }
            else
            {
                knowledgeBase.MarkIndexFailed(SystemActor);
            }

            notificationPublisher.Publish(new NotificationRequest(
                succeeded ? NotificationTypeKeys.KnowledgeBaseIndexingCompleted : NotificationTypeKeys.KnowledgeBaseIndexingFailed,
                new NotificationRecipient.User(knowledgeBase.OwnerId),
                succeeded
                    ? new Dictionary<string, string?> { ["knowledgeBaseName"] = knowledgeBase.Name }
                    : new Dictionary<string, string?> { ["knowledgeBaseName"] = knowledgeBase.Name, ["failureSummary"] = failureReason },
                new RelatedItem("KnowledgeBase", knowledgeBase.Id.ToString()),
                EventKey: $"knowledge-base:{knowledgeBase.Id}:indexing:{DateTime.UtcNow.Ticks}"));

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await retrievalIndexingNotifier.NotifyIndexStatusChangedAsync(knowledgeBase.OwnerId, knowledgeBase.Id, knowledgeBase.IndexStatus.ToString(), cancellationToken);
        }
    }
}

internal static partial class KnowledgeBaseIndexingJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexing job {IndexingJobId} not found — skipping.")]
    public static partial void JobNotFound(ILogger logger, Guid indexingJobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge base {KnowledgeBaseId} not found for indexing job — skipping.")]
    public static partial void KnowledgeBaseNotFound(ILogger logger, Guid knowledgeBaseId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexing job {IndexingJobId} failed.")]
    public static partial void IndexingFailed(ILogger logger, Exception exception, Guid indexingJobId);
}
