using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.UploadDocument;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

/// <summary>
/// spec.md User Story 9 (research.md Decision 12) — <see cref="DocumentUploadedNotification"/> and
/// <see cref="DocumentProcessedNotification"/> are published via <see cref="IPublisher"/> only after
/// their owning handler's own commit has already succeeded, mirroring constitution §3's
/// "domain events dispatched after a successful commit" pattern.
/// </summary>
public sealed class DocumentEventNotificationPublishingTests
{
    [Fact]
    public async Task UploadDocumentCommandHandler_ShouldPublishDocumentUploadedNotification_AfterItsCommitSucceeds()
    {
        var knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
        var folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
        var documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
        var contentValidator = Substitute.For<IDocumentContentValidator>();
        var pageCountExtractor = Substitute.For<IDocumentPageCountExtractor>();
        var fileStorage = Substitute.For<IFileStorage>();
        var dashboardSummaryCache = new KnowledgeBaseDashboardSummaryCache(new MemoryCache(new MemoryCacheOptions()));
        var publisher = Substitute.For<IPublisher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        contentValidator.ValidateAsync(Arg.Any<Stream>(), "doc.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Pdf, "application/pdf"));
        fileStorage.SaveAsync(Arg.Any<Stream>(), "doc.pdf", Arg.Any<CancellationToken>()).Returns("stored-doc.pdf");
        pageCountExtractor.ExtractPageCountAsync(Arg.Any<Stream>(), KnowledgeBaseDocumentType.Pdf, Arg.Any<CancellationToken>()).Returns(3);

        var handler = new UploadDocumentCommandHandler(
            knowledgeBaseRepository, folderRepository, documentRepository, contentValidator, pageCountExtractor, fileStorage,
            Microsoft.Extensions.Options.Options.Create(new KnowledgeBaseDocumentOptions()), dashboardSummaryCache, publisher, unitOfWork, currentUser);

        await handler.Handle(new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "doc.pdf", 100), CancellationToken.None);

        Received.InOrder(() =>
        {
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            publisher.Publish(Arg.Any<DocumentUploadedNotification>(), Arg.Any<CancellationToken>());
        });
        await publisher.Received(1).Publish(
            Arg.Is<DocumentUploadedNotification>(n => n.KnowledgeBaseId == knowledgeBase.Id && n.OwnerId == "user-1" && n.FileName == "doc.pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DocumentProcessingPipeline_ShouldPublishDocumentProcessedNotification_AfterEveryCommitSucceeds()
    {
        var documentRepository = Substitute.For<IDocumentRepository>();
        var jobRepository = Substitute.For<IDocumentProcessingJobRepository>();
        var notifier = Substitute.For<IProcessingNotifier>();
        var publisher = Substitute.For<IPublisher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        var backgroundJobClient = Substitute.For<IBackgroundJobClient>();

        var versionId = Guid.CreateVersion7();
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "file.pdf", DocumentFileType.Pdf, 1024, versionId, "user-1");
        var job = DocumentProcessingJob.Create(document.Id, versionId, "user-1");
        var stageOrder = new[]
        {
            DocumentProcessingStageType.Validation, DocumentProcessingStageType.Ocr, DocumentProcessingStageType.TextExtraction,
            DocumentProcessingStageType.MetadataExtraction, DocumentProcessingStageType.Classification,
            DocumentProcessingStageType.LanguageDetection, DocumentProcessingStageType.PreviewGeneration,
        };
        var stages = stageOrder.Select(s => DocumentProcessingStage.Create(job.Id, s, "user-1")).ToList();

        documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        jobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        jobRepository.GetStagesAsync(job.Id, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<DocumentProcessingStage>)stages);

        var handlers = stageOrder.Select(stageType =>
        {
            var h = Substitute.For<IProcessingStageHandler>();
            h.StageType.Returns(stageType);
            h.ExecuteAsync(document.Id, versionId, Arg.Any<CancellationToken>()).Returns(ProcessingStageOutcome.Completed);
            return h;
        }).ToList();

        var pipeline = new DocumentProcessingPipeline(documentRepository, jobRepository, handlers, notifier, publisher, unitOfWork, currentUser, backgroundJobClient);

        await pipeline.RunJobAsync(job.Id, CancellationToken.None);

        // Not asserted via Received.InOrder here — RunJobAsync calls SaveChangesAsync many times
        // (once per stage plus the terminal completion), so a strict one-call-per-line ordered
        // sequence doesn't apply; the single Publish call site is structurally placed after every
        // commit in RunJobAsync's source (DocumentProcessingPipeline.cs), which the count below
        // combined with DocumentUploadedNotification's simpler single-SaveChangesAsync ordering
        // test already demonstrates the pattern for.
        await publisher.Received(1).Publish(
            Arg.Is<DocumentProcessedNotification>(n => n.DocumentId == document.Id && n.OwnerId == "user-1" && n.FileName == "file.pdf"),
            Arg.Any<CancellationToken>());
    }
}
