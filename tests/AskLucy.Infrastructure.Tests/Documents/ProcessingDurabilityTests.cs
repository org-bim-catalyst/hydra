using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Hangfire;
using MediatR;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Documents;

/// <summary>
/// T062 — simulates a crash mid-pipeline (the process dies after some stages already committed
/// their <c>Completed</c>/<c>Skipped</c> state to the database, before the job as a whole
/// finished) and confirms that re-invoking <see cref="DocumentProcessingPipeline.RunJobAsync"/> —
/// exactly what Hangfire's own crash recovery does by re-running an interrupted job — resumes
/// from the first non-finished stage rather than re-executing already-finished ones
/// (FR-030a, research.md Decision 10). Lives here rather than as a true Hangfire-storage
/// integration test (killing a real <c>BackgroundJobServer</c> mid-job and letting Hangfire's own
/// recovery re-invoke it) because <c>DocumentProcessingStage</c>'s row in the database — not
/// Hangfire's internal state — is what <see cref="DocumentProcessingPipeline"/> actually consults
/// to decide what's already done; that decision is exactly what this test exercises directly.
/// </summary>
public sealed class ProcessingDurabilityTests
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
    public async Task RunJobAsync_ShouldNeverReExecuteAnAlreadyCompletedStage_WhenResumedAfterASimulatedCrash()
    {
        var (document, job, stages) = SetUpJob();

        // Simulate the process having crashed after Validation and Ocr already committed as
        // finished, but before the job as a whole completed.
        stages.Single(s => s.StageType == DocumentProcessingStageType.Validation).Start("user-1");
        stages.Single(s => s.StageType == DocumentProcessingStageType.Validation).Complete("user-1");
        stages.Single(s => s.StageType == DocumentProcessingStageType.Ocr).Start("user-1");
        stages.Single(s => s.StageType == DocumentProcessingStageType.Ocr).Skip("user-1");

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _handlers[DocumentProcessingStageType.Validation].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handlers[DocumentProcessingStageType.Ocr].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await _handlers[DocumentProcessingStageType.TextExtraction].Received(1)
            .ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>());
        await _handlers[DocumentProcessingStageType.PreviewGeneration].Received(1)
            .ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>());

        stages.Single(s => s.StageType == DocumentProcessingStageType.Validation).Status.Should().Be(DocumentProcessingStageStatus.Completed);
        stages.Single(s => s.StageType == DocumentProcessingStageType.Ocr).Status.Should().Be(DocumentProcessingStageStatus.Skipped);
        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
        document.ProcessingStatus.Should().Be(DocumentProcessingStatus.Completed);
    }

    [Fact]
    public async Task RunJobAsync_ShouldResumeFromExactlyTheFirstNonFinishedStage_WhenResumedMultipleStagesIn()
    {
        var (document, job, stages) = SetUpJob();

        foreach (var stageType in new[] { DocumentProcessingStageType.Validation, DocumentProcessingStageType.Ocr, DocumentProcessingStageType.TextExtraction, DocumentProcessingStageType.MetadataExtraction })
        {
            stages.Single(s => s.StageType == stageType).Start("user-1");
            stages.Single(s => s.StageType == stageType).Complete("user-1");
        }

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _handlers[DocumentProcessingStageType.MetadataExtraction].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handlers[DocumentProcessingStageType.Classification].Received(1)
            .ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>());
        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
    }
}
