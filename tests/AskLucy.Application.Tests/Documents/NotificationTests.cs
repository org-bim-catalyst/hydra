using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Commands.CompleteUpload;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Commands.ReplaceDocument;
using AskLucy.Application.Documents.Commands.StartUpload;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T117 — a <see cref="DocumentNotification"/> is created and pushed for all six event types (FR-047); reaching the storage limit blocks further upload and fires `StorageLimitReached` (FR-011).</summary>
public sealed class NotificationTests
{
    private static IOptions<DocumentUploadOptions> UploadOptions() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentUploadOptions { MaxFileSizeBytes = 10_000, ChunkSizeBytes = 256 });

    private static IOptions<DocumentStorageQuotaOptions> QuotaOptions(long defaultQuotaBytes = 10L * 1024 * 1024 * 1024) =>
        Microsoft.Extensions.Options.Options.Create(new DocumentStorageQuotaOptions { DefaultQuotaBytes = defaultQuotaBytes });

    [Fact]
    public async Task CompleteUpload_ShouldFireUploadCompletedNotification_OnSuccess()
    {
        var sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
        var resumableStorage = Substitute.For<IResumableUploadStorage>();
        var fileValidator = Substitute.For<IDocumentFileValidator>();
        var fileStorage = Substitute.For<IFileStorage>();
        var documentRepository = Substitute.For<IDocumentRepository>();
        var statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
        var processingNotifier = Substitute.For<IProcessingNotifier>();
        var processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        currentUser.UserId.Returns("user-1");
        statisticsRepository.ComputeAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));

        var session = DocumentUploadSession.Create("user-1", "report.pdf", 20, 256, DateTime.UtcNow.AddHours(1), "user-1");
        sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(20L);
        resumableStorage.OpenReadAsync(session.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4"))));
        fileValidator.ValidateAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        fileStorage.SaveAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>()).Returns("stored.pdf");
        documentRepository.FindDocumentIdByChecksumAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var finalizer = new DocumentUploadFinalizer(
            fileValidator, fileStorage, documentRepository, statisticsRepository, processingNotifier, UploadOptions(), QuotaOptions());
        var handler = new CompleteUploadCommandHandler(
            sessionRepository, resumableStorage, finalizer, processingPipeline, processingNotifier, unitOfWork, currentUser);

        await handler.Handle(new CompleteUploadCommand(session.Id), CancellationToken.None);

        await processingNotifier.Received(1).NotifyAsync(
            "user-1", DocumentNotificationEventType.UploadCompleted, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceDocument_ShouldFireVersionCreatedNotification_OnSuccess()
    {
        var sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
        var resumableStorage = Substitute.For<IResumableUploadStorage>();
        var documentRepository = Substitute.For<IDocumentRepository>();
        var fileValidator = Substitute.For<IDocumentFileValidator>();
        var fileStorage = Substitute.For<IFileStorage>();
        var processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
        var processingNotifier = Substitute.For<IProcessingNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        currentUser.UserId.Returns("user-1");
        var currentVersion = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "stored-v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(currentVersion.DocumentId, "user-1", "report.pdf", DocumentFileType.Pdf, 1000, currentVersion.Id, "user-1");
        documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        documentRepository.GetVersionByIdAsync(currentVersion.Id, Arg.Any<CancellationToken>()).Returns(currentVersion);

        var session = DocumentUploadSession.Create("user-1", "report-v2.pdf", 2000, 256, DateTime.UtcNow.AddHours(1), "user-1", document.Id);
        sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(2000L);
        resumableStorage.OpenReadAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(new MemoryStream());
        fileValidator.ValidateAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        fileStorage.SaveAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>()).Returns("stored-v2.pdf");

        var handler = new ReplaceDocumentCommandHandler(
            sessionRepository, resumableStorage, documentRepository, fileValidator, fileStorage,
            processingPipeline, processingNotifier, unitOfWork, currentUser);

        await handler.Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        await processingNotifier.Received(1).NotifyAsync(
            "user-1", DocumentNotificationEventType.VersionCreated, document.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartUpload_ShouldFireStorageLimitReachedNotification_AndRejectTheUpload_WhenQuotaExceeded()
    {
        var sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
        var documentRepository = Substitute.For<IDocumentRepository>();
        var statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
        var processingNotifier = Substitute.For<IProcessingNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        currentUser.UserId.Returns("user-1");
        // Already at (10 GB - 100 bytes) of a 10 GB quota; even a small upload pushes past the limit.
        statisticsRepository.ComputeAggregateAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(50, 10L * 1024 * 1024 * 1024 - 100, null, "{}", "{}"));

        var handler = new StartUploadCommandHandler(
            sessionRepository, documentRepository, statisticsRepository, processingNotifier, UploadOptions(), QuotaOptions(), unitOfWork, currentUser);

        var act = () => handler.Handle(new StartUploadCommand("small-but-over-quota.pdf", 500), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await processingNotifier.Received(1).NotifyAsync(
            "user-1", DocumentNotificationEventType.StorageLimitReached, null, Arg.Any<string>(), Arg.Any<CancellationToken>());
        sessionRepository.DidNotReceive().Add(Arg.Any<DocumentUploadSession>());
    }
}
