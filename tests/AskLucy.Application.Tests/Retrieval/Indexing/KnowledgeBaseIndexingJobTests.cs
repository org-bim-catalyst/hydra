using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Retrieval.Indexing;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AskLucy.Application.Tests.Retrieval.Indexing;

/// <summary>T235 — <see cref="KnowledgeBaseIndexingJob"/>, exercised directly (mirrors <c>DocumentProcessingPipelineTests</c>).</summary>
public sealed class KnowledgeBaseIndexingJobTests
{
    private readonly IIndexingJobRepository _indexingJobRepository = Substitute.For<IIndexingJobRepository>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseDocumentRepository _knowledgeBaseDocumentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IIndexingOrchestrator _orchestrator = Substitute.For<IIndexingOrchestrator>();
    private readonly IRetrievalIndexingNotifier _retrievalIndexingNotifier = Substitute.For<IRetrievalIndexingNotifier>();
    private readonly INotificationPublisher _notificationPublisher = Substitute.For<INotificationPublisher>();
    private readonly IOperationalFailureRecorder _operationalFailureRecorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private (KnowledgeBase KnowledgeBase, KnowledgeBaseDocument Document, IndexingJob Job) SetUpJob(bool hasJobInProgress = false)
    {
        var knowledgeBase = KnowledgeBase.Create("Construction Standards", "user-1", "user-1");
        var document = KnowledgeBaseDocument.Create(knowledgeBase.Id, null, "spec.md", "stored.md", "text/markdown", 1024, null, "user-1");
        document.LinkToDocument(Guid.CreateVersion7(), "user-1");
        var job = IndexingJob.Create(knowledgeBase.Id, document.Id, IndexingJobType.SingleDocumentIndex, 3, "user-1");

        _indexingJobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _knowledgeBaseDocumentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _indexingJobRepository.HasJobInProgressAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(hasJobInProgress);

        return (knowledgeBase, document, job);
    }

    private KnowledgeBaseIndexingJob CreateSut() => new(
        _indexingJobRepository, _knowledgeBaseRepository, _knowledgeBaseDocumentRepository, _orchestrator,
        _retrievalIndexingNotifier, _notificationPublisher, _operationalFailureRecorder, _unitOfWork,
        Substitute.For<ILogger<KnowledgeBaseIndexingJob>>());

    [Fact]
    public async Task RunAsync_ShouldCompleteJobAndSettleKnowledgeBase_WhenOrchestratorSucceeds()
    {
        var (knowledgeBase, document, job) = SetUpJob();
        _orchestrator.IndexKnowledgeBaseDocumentAsync(document.Id, false, Arg.Any<CancellationToken>()).Returns(IndexingOutcome.Completed);

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Completed);
        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.Indexed);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r.Type == NotificationTypeKeys.DocumentIndexingCompleted && r.EventKey == $"indexing-job:{job.Id}:completed"));
        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r.Type == NotificationTypeKeys.KnowledgeBaseIndexingCompleted && r.RelatedItem!.Type == "KnowledgeBase" && r.RelatedItem.Id == knowledgeBase.Id.ToString()));
        await _retrievalIndexingNotifier.Received(1).NotifyIndexStatusChangedAsync(knowledgeBase.OwnerId, knowledgeBase.Id, "Indexed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldMarkPartiallyIndexed_WhenOrchestratorReportsPartial()
    {
        var (knowledgeBase, document, job) = SetUpJob();
        _orchestrator.IndexKnowledgeBaseDocumentAsync(document.Id, false, Arg.Any<CancellationToken>()).Returns(IndexingOutcome.PartiallyCompleted);

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Completed);
        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.PartiallyIndexed);
    }

    [Fact]
    public async Task RunAsync_ShouldFailJobAndKnowledgeBaseAndRecordOperationalFailure_WhenOrchestratorReportsFailed()
    {
        var (knowledgeBase, document, job) = SetUpJob();
        _orchestrator.IndexKnowledgeBaseDocumentAsync(document.Id, false, Arg.Any<CancellationToken>()).Returns(IndexingOutcome.Failed);

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Failed);
        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.Failed);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r.Type == NotificationTypeKeys.DocumentIndexingFailed));
        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r.Type == NotificationTypeKeys.KnowledgeBaseIndexingFailed));
        _operationalFailureRecorder.Received(1).Record(Arg.Any<OperationalFailureReport>());
    }

    [Fact]
    public async Task RunAsync_ShouldFailJob_WhenOrchestratorThrows()
    {
        var (knowledgeBase, document, job) = SetUpJob();
        _orchestrator.IndexKnowledgeBaseDocumentAsync(document.Id, false, Arg.Any<CancellationToken>())
            .Returns<IndexingOutcome>(_ => throw new InvalidOperationException("vendor body with secrets"));

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Failed);
        job.FailureReason.Should().NotContain("vendor body with secrets");
    }

    [Fact]
    public async Task RunAsync_ShouldNotSettleKnowledgeBase_WhenAnotherJobIsStillInProgress()
    {
        var (knowledgeBase, document, job) = SetUpJob(hasJobInProgress: true);
        _orchestrator.IndexKnowledgeBaseDocumentAsync(document.Id, false, Arg.Any<CancellationToken>()).Returns(IndexingOutcome.Completed);

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Completed);
        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.NotIndexed);
        _notificationPublisher.DidNotReceive().Publish(Arg.Is<NotificationRequest>(r => r.Type == NotificationTypeKeys.KnowledgeBaseIndexingCompleted));
    }

    [Fact]
    public async Task RunAsync_ShouldFailWithDocumentRemovedReasonAndPublishNothing_WhenKnowledgeBaseDocumentIsGone()
    {
        var knowledgeBase = KnowledgeBase.Create("Construction Standards", "user-1", "user-1");
        var missingDocumentId = Guid.CreateVersion7();
        var job = IndexingJob.Create(knowledgeBase.Id, missingDocumentId, IndexingJobType.SingleDocumentIndex, 3, "user-1");

        _indexingJobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _knowledgeBaseDocumentRepository.GetByIdAsync(missingDocumentId, Arg.Any<CancellationToken>()).Returns((KnowledgeBaseDocument?)null);

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(IndexingJobStatus.Failed);
        job.FailureReason.Should().Contain("removed");
        _notificationPublisher.DidNotReceiveWithAnyArgs().Publish(default!);
    }

    [Fact]
    public async Task RunAsync_ShouldBeANoOp_WhenJobAlreadySettled()
    {
        var (knowledgeBase, document, job) = SetUpJob();
        job.Fail("already failed", "user-1");

        await CreateSut().RunAsync(job.Id, CancellationToken.None);

        await _orchestrator.DidNotReceiveWithAnyArgs().IndexKnowledgeBaseDocumentAsync(default, default, default);
        _notificationPublisher.DidNotReceiveWithAnyArgs().Publish(default!);
    }
}
