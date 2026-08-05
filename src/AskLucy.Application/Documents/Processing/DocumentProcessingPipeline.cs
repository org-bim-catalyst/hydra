using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Hangfire;

namespace AskLucy.Application.Documents.Processing;

/// <summary>
/// <see cref="IDocumentProcessingPipeline"/> implementation (US2, tasks.md T073). Chains the 7
/// <see cref="IProcessingStageHandler"/> executions via Hangfire (research.md Decision 2),
/// persisting <see cref="DocumentProcessingStage"/> state before/after each stage so
/// <see cref="RunJobAsync"/> — re-invoked by Hangfire's own crash recovery after a restart —
/// always resumes from the first non-<c>Completed</c>/non-<c>Skipped</c> stage rather than
/// redoing finished work (FR-030a, research.md Decision 10). The database row, not Hangfire's
/// internal state, is the source of truth for "what's done." Schedules via the injected
/// <see cref="IBackgroundJobClient"/> rather than the static <c>Hangfire.BackgroundJob</c>
/// facade — Hangfire's own documentation recommends the service-based API precisely because the
/// static one requires a live <c>JobStorage.Current</c>, which makes <see cref="ScheduleAsync"/>
/// impossible to unit test without one.
/// </summary>
public sealed class DocumentProcessingPipeline(
    IDocumentRepository documentRepository,
    IDocumentProcessingJobRepository jobRepository,
    IEnumerable<IProcessingStageHandler> stageHandlers,
    IProcessingNotifier notifier,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IBackgroundJobClient backgroundJobClient) : IDocumentProcessingPipeline
{
    private const string SystemActor = "system:processing";

    private static readonly DocumentProcessingStageType[] StageOrder =
    [
        DocumentProcessingStageType.Validation,
        DocumentProcessingStageType.Ocr,
        DocumentProcessingStageType.TextExtraction,
        DocumentProcessingStageType.MetadataExtraction,
        DocumentProcessingStageType.Classification,
        DocumentProcessingStageType.LanguageDetection,
        DocumentProcessingStageType.PreviewGeneration,
    ];

    public async Task EnqueueAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var actor = currentUser.UserId ?? SystemActor;

        var job = DocumentProcessingJob.Create(documentId, documentVersionId, actor);
        jobRepository.Add(job);

        foreach (var stageType in StageOrder)
        {
            jobRepository.AddStage(DocumentProcessingStage.Create(job.Id, stageType, actor));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await ScheduleAsync(job, actor, cancellationToken);
    }

    public async Task RetryAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var actor = currentUser.UserId ?? SystemActor;

        var job = await jobRepository.GetCurrentForDocumentAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("No processing job exists for this document.");

        job.Retry(actor);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await ScheduleAsync(job, actor, cancellationToken);
    }

    private async Task ScheduleAsync(DocumentProcessingJob job, string actor, CancellationToken cancellationToken)
    {
        // Hangfire's serializer captures the expression's arguments at schedule time, not by
        // reference — CancellationToken.None here is intentional: Hangfire supplies its own
        // shutdown-aware token to the running job, this parameter only exists to satisfy the
        // interface signature the expression tree captures.
        var hangfireJobId = backgroundJobClient.Enqueue<IDocumentProcessingPipeline>(p => p.RunJobAsync(job.Id, CancellationToken.None));

        job.Start(hangfireJobId, actor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RunJobAsync(Guid documentProcessingJobId, CancellationToken cancellationToken = default)
    {
        var job = await jobRepository.GetByIdAsync(documentProcessingJobId, cancellationToken)
            ?? throw new KeyNotFoundException("Processing job not found.");
        var document = await documentRepository.GetByIdAsync(job.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");
        var stages = await jobRepository.GetStagesAsync(job.Id, cancellationToken);

        if (document.ProcessingStatus != DocumentProcessingStatus.Processing)
        {
            document.SetProcessingStatus(DocumentProcessingStatus.Processing, SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        foreach (var stageType in StageOrder)
        {
            var stage = stages.First(s => s.StageType == stageType);

            // Resume after a crash/restart: never redo a stage already finished (FR-030a).
            if (stage.Status is DocumentProcessingStageStatus.Completed or DocumentProcessingStageStatus.Skipped)
            {
                continue;
            }

            var handler = stageHandlers.First(h => h.StageType == stageType);

            stage.Start(SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await NotifyStageAsync(document, job.Id, stageType, DocumentProcessingStageStatus.InProgress, cancellationToken);

            try
            {
                var outcome = await handler.ExecuteAsync(job.DocumentId, job.DocumentVersionId, cancellationToken);

                if (outcome == ProcessingStageOutcome.Skipped)
                {
                    stage.Skip(SystemActor);
                }
                else
                {
                    stage.Complete(SystemActor);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                await NotifyStageAsync(document, job.Id, stageType, stage.Status, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FailAsync(job, document, stageType, stage, ex.Message, cancellationToken);
                return; // Stop the chain — later stages never run for a failed job.
            }
        }

        job.Complete(SystemActor);
        document.SetProcessingStatus(DocumentProcessingStatus.Completed, SystemActor);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        jobRepository.AddLog(DocumentProcessingLog.Create(document.Id, job.Id, "ProcessingCompleted", null, SystemActor));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyProcessingCompletedAsync(document.OwnerId, document.Id, cancellationToken);
        await notifier.NotifyAsync(document.OwnerId, DocumentNotificationEventType.ProcessingCompleted, document.Id, $"\"{document.FileName}\" finished processing.", cancellationToken);
    }

    private async Task NotifyStageAsync(Domain.Documents.Document document, Guid jobId, DocumentProcessingStageType stageType, DocumentProcessingStageStatus status, CancellationToken cancellationToken)
    {
        jobRepository.AddLog(DocumentProcessingLog.Create(document.Id, jobId, $"Stage{stageType}{status}", null, SystemActor));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyStageChangedAsync(document.OwnerId, document.Id, stageType, status, cancellationToken);
    }

    private async Task FailAsync(
        DocumentProcessingJob job, Domain.Documents.Document document, DocumentProcessingStageType stageType,
        DocumentProcessingStage stage, string failureReason, CancellationToken cancellationToken)
    {
        stage.Fail(failureReason, SystemActor);
        job.Fail(failureReason, SystemActor);
        document.SetProcessingStatus(DocumentProcessingStatus.Failed, SystemActor);

        jobRepository.AddLog(DocumentProcessingLog.Create(document.Id, job.Id, $"Stage{stageType}Failed", failureReason, SystemActor));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyStageChangedAsync(document.OwnerId, document.Id, stageType, DocumentProcessingStageStatus.Failed, cancellationToken);
        await notifier.NotifyProcessingFailedAsync(document.OwnerId, document.Id, failureReason, cancellationToken);

        var eventType = stageType == DocumentProcessingStageType.Ocr
            ? DocumentNotificationEventType.OcrFailed
            : DocumentNotificationEventType.ProcessingFailed;
        await notifier.NotifyAsync(document.OwnerId, eventType, document.Id, $"\"{document.FileName}\" failed to process: {failureReason}", cancellationToken);
    }
}
