using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MediatR;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents.Processing;

/// <summary>
/// T060 — a full pipeline run (faked <see cref="IProcessingStageHandler"/>s) proceeds
/// <c>Queued</c> → <c>Completed</c>, with a stage correctly <c>Skipped</c> when its handler
/// determines nothing was needed (e.g. OCR on a PDF that already has a text layer). Exercises
/// <see cref="DocumentProcessingPipeline.RunJobAsync"/> directly — exactly what a real Hangfire
/// worker invokes — rather than through <see cref="DocumentProcessingPipeline.EnqueueAsync"/>,
/// which is covered by <c>ProcessingFailureAndRetryTests</c>'s retry-scheduling assertions
/// instead (this class focuses on the run/resume logic, not the scheduling call).
/// </summary>
public sealed class DocumentProcessingPipelineTests
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
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
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
        new(_documentRepository, _jobRepository, _handlers.Values, _notifier, _publisher, _unitOfWork, _currentUser, _backgroundJobClient);

    [Fact]
    public async Task RunJobAsync_ShouldCompleteTheJobAndDocument_WhenEveryStageSucceeds()
    {
        var (document, job, stages) = SetUpJob();

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
        document.ProcessingStatus.Should().Be(DocumentProcessingStatus.Completed);
        stages.Should().OnlyContain(s => s.Status == DocumentProcessingStageStatus.Completed);
        await _notifier.Received(1).NotifyProcessingCompletedAsync(document.OwnerId, document.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunJobAsync_ShouldMarkOcrStageSkipped_WhenItsHandlerReportsSkipped()
    {
        var (document, job, stages) = SetUpJob();
        _handlers[DocumentProcessingStageType.Ocr].ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>())
            .Returns(ProcessingStageOutcome.Skipped);

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        stages.Single(s => s.StageType == DocumentProcessingStageType.Ocr).Status.Should().Be(DocumentProcessingStageStatus.Skipped);
        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
    }
}
