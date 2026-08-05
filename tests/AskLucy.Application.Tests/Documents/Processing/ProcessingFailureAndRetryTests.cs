using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents.Processing;

/// <summary>
/// T061 — a failing stage lands the job in <c>Failed</c> with a specific <c>failureReason</c>
/// (FR-028); <see cref="DocumentProcessingJob.Retry"/> (invoked by
/// <see cref="DocumentProcessingPipeline.RetryAsync"/>, and by extension the
/// <c>RetryProcessing</c> command) refuses to retry a job that isn't currently <c>Failed</c>
/// (FR-029, Edge Cases). A successful retry reaches <see cref="IBackgroundJobClient.Create"/> —
/// faked here rather than requiring a live Hangfire storage backend, since
/// <see cref="DocumentProcessingPipeline"/> depends on the injectable
/// <see cref="IBackgroundJobClient"/> (Hangfire's own recommended, testable API) instead of the
/// static <c>Hangfire.BackgroundJob</c> facade.
/// </summary>
public sealed class ProcessingFailureAndRetryTests
{
    private static readonly DocumentProcessingStageType[] AllStages =
    [
        DocumentProcessingStageType.Validation,
        DocumentProcessingStageType.Ocr,
        DocumentProcessingStageType.TextExtraction,
        DocumentProcessingStageType.MetadataExtraction,
        DocumentProcessingStageType.Classification,
        DocumentProcessingStageType.LanguageDetection,
        DocumentProcessingStageType.PreviewGeneration,
    ];

    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentProcessingJobRepository _jobRepository = Substitute.For<IDocumentProcessingJobRepository>();
    private readonly IProcessingNotifier _notifier = Substitute.For<IProcessingNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly Dictionary<DocumentProcessingStageType, IProcessingStageHandler> _handlers = [];

    private (Document Document, DocumentProcessingJob Job, List<DocumentProcessingStage> Stages) SetUpJob()
    {
        var versionId = Guid.CreateVersion7();
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "file.pdf", DocumentFileType.Pdf, 1024, versionId, "user-1");
        var job = DocumentProcessingJob.Create(document.Id, versionId, "user-1");
        var stages = AllStages.Select(s => DocumentProcessingStage.Create(job.Id, s, "user-1")).ToList();

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _jobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _jobRepository.GetStagesAsync(job.Id, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<DocumentProcessingStage>)stages);
        _jobRepository.GetCurrentForDocumentAsync(document.Id, Arg.Any<CancellationToken>()).Returns(job);
        _backgroundJobClient.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("hangfire-job-1");

        _handlers.Clear();
        foreach (var stageType in AllStages)
        {
            var handler = Substitute.For<IProcessingStageHandler>();
            handler.StageType.Returns(stageType);
            handler.ExecuteAsync(document.Id, versionId, Arg.Any<CancellationToken>()).Returns(ProcessingStageOutcome.Completed);
            _handlers[stageType] = handler;
        }

        return (document, job, stages);
    }

    private DocumentProcessingPipeline CreateSut() =>
        new(_documentRepository, _jobRepository, _handlers.Values, _notifier, _unitOfWork, _currentUser, _backgroundJobClient);

    [Fact]
    public async Task RunJobAsync_ShouldFailTheJobWithASpecificReason_WhenAStageThrows()
    {
        var (document, job, stages) = SetUpJob();
        _handlers[DocumentProcessingStageType.TextExtraction].ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>())
            .Returns<ProcessingStageOutcome>(_ => throw new InvalidOperationException("The file could not be parsed."));

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(DocumentProcessingJobStatus.Failed);
        job.FailureReason.Should().Be("The file could not be parsed.");
        document.ProcessingStatus.Should().Be(DocumentProcessingStatus.Failed);
        stages.Single(s => s.StageType == DocumentProcessingStageType.TextExtraction).FailureReason.Should().Be("The file could not be parsed.");
        await _notifier.Received(1).NotifyProcessingFailedAsync(document.OwnerId, document.Id, "The file could not be parsed.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunJobAsync_ShouldFireOcrFailedEventType_WhenTheOcrStageFails()
    {
        var (document, job, _) = SetUpJob();
        _handlers[DocumentProcessingStageType.Ocr].ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>())
            .Returns<ProcessingStageOutcome>(_ => throw new InvalidOperationException("OCR engine crashed."));

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _notifier.Received(1).NotifyAsync(
            document.OwnerId, DocumentNotificationEventType.OcrFailed, document.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(
            document.OwnerId, DocumentNotificationEventType.ProcessingFailed, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunJobAsync_ShouldFireProcessingFailedEventType_WhenANonOcrStageFails()
    {
        var (document, job, _) = SetUpJob();
        _handlers[DocumentProcessingStageType.TextExtraction].ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>())
            .Returns<ProcessingStageOutcome>(_ => throw new InvalidOperationException("The file could not be parsed."));

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _notifier.Received(1).NotifyAsync(
            document.OwnerId, DocumentNotificationEventType.ProcessingFailed, document.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunJobAsync_ShouldNeverRunLaterStages_AfterAStageFails()
    {
        var (document, job, stages) = SetUpJob();
        _handlers[DocumentProcessingStageType.Ocr].ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>())
            .Returns<ProcessingStageOutcome>(_ => throw new InvalidOperationException("OCR engine crashed."));

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _handlers[DocumentProcessingStageType.TextExtraction].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        stages.Single(s => s.StageType == DocumentProcessingStageType.TextExtraction).Status.Should().Be(DocumentProcessingStageStatus.Pending);
    }

    [Fact]
    public async Task RetryAsync_ShouldReEnqueueFromFirstNonCompletedStage_WhenJobIsFailed()
    {
        var (document, job, _) = SetUpJob();
        job.Fail("Something went wrong.", "system:processing");

        await CreateSut().RetryAsync(document.Id, CancellationToken.None);

        job.Status.Should().Be(DocumentProcessingJobStatus.InProgress);
        job.FailureReason.Should().BeNull();
        job.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task RetryAsync_ShouldThrowProcessingNotInFailedState_WhenJobIsNotFailed()
    {
        var (document, _, _) = SetUpJob(); // Job defaults to Queued.

        var act = () => CreateSut().RetryAsync(document.Id, CancellationToken.None);

        await act.Should().ThrowAsync<ProcessingNotInFailedStateException>();
    }

    [Fact]
    public async Task RetryAsync_ShouldThrowProcessingNotInFailedState_WhenJobIsInProgress()
    {
        var (document, job, _) = SetUpJob();
        job.Start("hangfire-job-1", "system:processing");

        var act = () => CreateSut().RetryAsync(document.Id, CancellationToken.None);

        await act.Should().ThrowAsync<ProcessingNotInFailedStateException>();
    }
}
