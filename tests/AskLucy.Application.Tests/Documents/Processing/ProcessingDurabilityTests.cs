using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Hangfire;
using MediatR;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents.Processing;

/// <summary>
/// T062 — simulates a crash mid-stage and confirms already-<c>Completed</c> stages are never
/// re-executed on resume (FR-030a, research.md Decision 10). Correction from tasks.md: this
/// lives in <c>AskLucy.Application.Tests</c>, not <c>AskLucy.Infrastructure.Tests</c> as
/// originally planned — the resume/skip logic under test is entirely inside
/// <see cref="DocumentProcessingPipeline.RunJobAsync"/> (Application layer) and has no
/// dependency on Hangfire's actual storage/crash-recovery mechanics, which are a trusted
/// third-party concern, not something this codebase needs to re-verify. "Crash mid-stage" is
/// simulated the same way a real crash would leave the database: some
/// <see cref="DocumentProcessingStage"/> rows already <c>Completed</c>, then <c>RunJobAsync</c>
/// invoked again exactly as Hangfire's own recovery would re-invoke it.
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
    public async Task RunJobAsync_ShouldSkipAlreadyCompletedStages_AndOnlyRunTheRemainderOnResume()
    {
        var (document, job, stages) = SetUpJob();
        stages[0].Start("user-1");
        stages[0].Complete("user-1");
        stages[1].Start("user-1");
        stages[1].Complete("user-1");

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _handlers[DocumentProcessingStageType.Validation].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handlers[DocumentProcessingStageType.Ocr].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handlers[DocumentProcessingStageType.TextExtraction].Received(1)
            .ExecuteAsync(document.Id, job.DocumentVersionId, Arg.Any<CancellationToken>());
        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
    }

    [Fact]
    public async Task RunJobAsync_ShouldAlsoSkipStages_ThatWereMarkedSkippedBeforeTheCrash()
    {
        var (document, job, stages) = SetUpJob();
        stages[0].Start("user-1");
        stages[0].Complete("user-1");
        stages[1].Start("user-1");
        stages[1].Skip("user-1"); // e.g. OCR already determined "not needed" before the crash.

        await CreateSut().RunJobAsync(job.Id, CancellationToken.None);

        await _handlers[DocumentProcessingStageType.Ocr].DidNotReceive()
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        stages[1].Status.Should().Be(DocumentProcessingStageStatus.Skipped);
        job.Status.Should().Be(DocumentProcessingJobStatus.Completed);
    }
}
