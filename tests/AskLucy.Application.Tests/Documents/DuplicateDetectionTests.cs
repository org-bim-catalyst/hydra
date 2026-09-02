using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Commands.CompleteUpload;
using AskLucy.Application.Documents.Commands.CompleteUploadAsNew;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Options;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

public sealed class DuplicateDetectionTests
{
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IResumableUploadStorage _resumableStorage = Substitute.For<IResumableUploadStorage>();
    private readonly IDocumentFileValidator _fileValidator = Substitute.For<IDocumentFileValidator>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly IProcessingNotifier _processingNotifier = Substitute.For<IProcessingNotifier>();
    private readonly IDocumentProcessingPipeline _processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public DuplicateDetectionTests()
    {
        _statisticsRepository.ComputeAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
    }

    private static IOptions<DocumentUploadOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentUploadOptions { MaxFileSizeBytes = 10_000 });

    private static IOptions<DocumentStorageQuotaOptions> QuotaOptions() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentStorageQuotaOptions());

    private DocumentUploadFinalizer CreateFinalizer() =>
        new(_fileValidator, _fileStorage, _documentRepository, _statisticsRepository, _processingNotifier, Options(), QuotaOptions());

    private DocumentUploadSession SetUpSessionReadyToComplete(long sizeBytes)
    {
        var session = DocumentUploadSession.Create("user-1", "report.pdf", sizeBytes, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(sizeBytes);
        _resumableStorage.OpenReadAsync(session.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 fake pdf content"))));
        return session;
    }

    [Fact]
    public async Task CompleteUpload_ShouldDetectDuplicate_AndNotCreateADocument()
    {
        _currentUser.UserId.Returns("user-1");
        var session = SetUpSessionReadyToComplete(20);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>()).Returns("stored-abc.pdf");
        var existingDocumentId = Guid.CreateVersion7();
        _documentRepository.FindDocumentIdByChecksumAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existingDocumentId);

        var handler = new CompleteUploadCommandHandler(
            _sessionRepository, _resumableStorage, CreateFinalizer(), _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var result = await handler.Handle(new CompleteUploadCommand(session.Id), CancellationToken.None);

        result.IsDuplicate.Should().BeTrue();
        result.DuplicateOfDocumentId.Should().Be(existingDocumentId);
        result.Document.Should().BeNull();
        _documentRepository.DidNotReceive().Add(Arg.Any<Document>());
        await _processingPipeline.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _resumableStorage.Received(1).DeleteAsync(session.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUpload_ShouldCreateDocumentAndEnqueueProcessing_WhenNoDuplicateFound()
    {
        _currentUser.UserId.Returns("user-1");
        var session = SetUpSessionReadyToComplete(20);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>()).Returns("stored-abc.pdf");
        _documentRepository.FindDocumentIdByChecksumAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var handler = new CompleteUploadCommandHandler(
            _sessionRepository, _resumableStorage, CreateFinalizer(), _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var result = await handler.Handle(new CompleteUploadCommand(session.Id), CancellationToken.None);

        result.IsDuplicate.Should().BeFalse();
        result.Document.Should().NotBeNull();
        result.Document!.FileName.Should().Be("report.pdf");
        _documentRepository.Received(1).Add(Arg.Any<Document>());
        await _processingPipeline.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadAsVersion_ShouldCreateNewVersionOfExistingDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 20, 256, DateTime.UtcNow.AddHours(1), "user-1");
        session.MarkPendingDuplicateResolution("stored-abc.pdf", "deadbeef", "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var existingVersion = DocumentVersion.Create(Guid.NewGuid(), 1, 0, "old-stored.pdf", "report.pdf", 10, Guid.NewGuid(), "user-1");
        var existingDocument = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 10, existingVersion.Id, "user-1");
        _documentRepository.GetByIdAsync(existingDocument.Id, Arg.Any<CancellationToken>()).Returns(existingDocument);
        _documentRepository.GetVersionByIdAsync(existingVersion.Id, Arg.Any<CancellationToken>()).Returns(existingVersion);

        var handler = new CompleteUploadAsVersionCommandHandler(
            _sessionRepository, _documentRepository, _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var result = await handler.Handle(
            new CompleteUploadAsVersionCommand(session.Id, existingDocument.Id, VersionIncrement.Minor), CancellationToken.None);

        result.Should().NotBeNull();
        existingDocument.CurrentVersionId.Should().NotBe(existingVersion.Id, "a new version was created and repointed");
        _documentRepository.Received(1).AddVersion(Arg.Is<DocumentVersion>(v => v != null && v.VersionMajor == 1 && v.VersionMinor == 1));
        await _processingPipeline.Received(1).EnqueueAsync(existingDocument.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadAsNew_ShouldCreateSeparateDocument_IgnoringTheDuplicate()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 20, 256, DateTime.UtcNow.AddHours(1), "user-1");
        session.MarkPendingDuplicateResolution("stored-abc.pdf", "deadbeef", "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _fileStorage.OpenReadAsync("stored-abc.pdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4"))));
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));

        var handler = new CompleteUploadAsNewCommandHandler(
            _sessionRepository, _documentRepository, _fileStorage, _fileValidator, _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var result = await handler.Handle(new CompleteUploadAsNewCommand(session.Id), CancellationToken.None);

        result.FileName.Should().Be("report.pdf");
        _documentRepository.Received(1).Add(Arg.Any<Document>());
        await _processingPipeline.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
