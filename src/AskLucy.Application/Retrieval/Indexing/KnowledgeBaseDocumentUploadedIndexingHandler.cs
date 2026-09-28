using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Domain.Retrieval;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Retrieval.Indexing;

/// <summary>
/// Enqueues a <see cref="IKnowledgeBaseIndexingJob"/> for every uploaded knowledge-base document
/// (specs/067-notifications-communication-hub T238; specs/016 R27 — before this handler,
/// <c>IIndexingOrchestrator</c> had no production caller and uploaded documents were never
/// indexed). <see cref="DocumentUploadedNotification"/> is published only after
/// <c>UploadDocumentCommandHandler</c>'s own commit succeeds, so this handler owns its own save
/// rather than joining one already in flight.
/// </summary>
public sealed class KnowledgeBaseDocumentUploadedIndexingHandler(
    IKnowledgeBaseDocumentRepository knowledgeBaseDocumentRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IIndexingJobRepository indexingJobRepository,
    IBackgroundJobClient backgroundJobClient,
    IOperationalFailureRecorder operationalFailureRecorder,
    IUnitOfWork unitOfWork,
    ILogger<KnowledgeBaseDocumentUploadedIndexingHandler> logger) : INotificationHandler<DocumentUploadedNotification>
{
    private const string SystemActor = "system:retrieval-indexing";

    public async Task Handle(DocumentUploadedNotification notification, CancellationToken cancellationToken)
    {
        var knowledgeBaseDocument = await knowledgeBaseDocumentRepository.GetByIdAsync(notification.DocumentId, cancellationToken);
        if (knowledgeBaseDocument is null || knowledgeBaseDocument.ProcessingStatus == KnowledgeBaseDocumentProcessingStatus.Failed)
        {
            // Upload-time validation/page-count extraction already failed this document — never
            // queue indexing work for something the user already saw fail.
            return;
        }

        var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(notification.KnowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Knowledge base not found.");

        var job = IndexingJob.Create(notification.KnowledgeBaseId, knowledgeBaseDocument.Id, IndexingJobType.SingleDocumentIndex, maxRetries: 3, SystemActor);
        indexingJobRepository.Add(job);
        knowledgeBase.MarkIndexing(SystemActor);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var hangfireJobId = backgroundJobClient.Enqueue<IKnowledgeBaseIndexingJob>(j => j.RunAsync(job.Id, CancellationToken.None));
            job.AssignHangfireJobId(hangfireJobId, SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            job.Fail("Indexing could not be scheduled.", SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            KnowledgeBaseDocumentUploadedIndexingHandlerLog.EnqueueFailed(logger, ex, job.Id);
            operationalFailureRecorder.Record(new OperationalFailureReport
            {
                Engine = OperationalFailureEngine.BackgroundJob,
                Operation = "Knowledge-base indexing",
                Kind = OperationalFailureKind.JobFailedAfterRetries,
                Reason = "Indexing could not be scheduled.",
                Exception = ex,
                Subject = new OperationalFailureSubject("KnowledgeBase", knowledgeBase.Id),
            });

            throw;
        }
    }
}

internal static partial class KnowledgeBaseDocumentUploadedIndexingHandlerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not enqueue indexing job {IndexingJobId}.")]
    public static partial void EnqueueFailed(ILogger logger, Exception exception, Guid indexingJobId);
}
