using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Commands.ReplaceDocument;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T103 — <c>ReplaceDocument</c> creates a new version, repoints `CurrentVersionId`; the prior version's file/content is untouched (FR-038).</summary>
public sealed class ReplaceDocumentTests
{
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IResumableUploadStorage _resumableStorage = Substitute.For<IResumableUploadStorage>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentFileValidator _fileValidator = Substitute.For<IDocumentFileValidator>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentProcessingPipeline _processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
    private readonly IProcessingNotifier _processingNotifier = Substitute.For<IProcessingNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private ReplaceDocumentCommandHandler CreateSut() => new(
        _sessionRepository, _resumableStorage, _documentRepository, _fileValidator, _fileStorage,
        _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

    private (Document Document, DocumentVersion CurrentVersion) SetUpDocument()
    {
        var currentVersion = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "stored-v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(currentVersion.DocumentId, "user-1", "report.pdf", DocumentFileType.Pdf, 1000, currentVersion.Id, "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetVersionByIdAsync(currentVersion.Id, Arg.Any<CancellationToken>()).Returns(currentVersion);
        return (document, currentVersion);
    }

    private DocumentUploadSession SetUpSession(Guid targetDocumentId, long declaredSizeBytes = 2000)
    {
        var session = DocumentUploadSession.Create("user-1", "report-v2.pdf", declaredSizeBytes, 256, DateTime.UtcNow.AddHours(1), "user-1", targetDocumentId);
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(declaredSizeBytes);
        _resumableStorage.OpenReadAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(new MemoryStream());
        return session;
    }

    [Fact]
    public async Task Handle_ShouldCreateNewVersionAndRepointCurrentVersionId_AndEnqueueProcessing()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, currentVersion) = SetUpDocument();
        var session = SetUpSession(document.Id);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>()).Returns("stored-v2.pdf");

        var result = await CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        document.CurrentVersionId.Should().NotBe(currentVersion.Id);
        document.ProcessingStatus.Should().Be(DocumentProcessingStatus.Queued);
        session.Status.Should().Be(DocumentUploadSessionStatus.Completed);
        result.Id.Should().Be(document.Id);

        _documentRepository.Received(1).AddVersion(Arg.Is<DocumentVersion>(v => v != null && v.VersionMajor == 1 && v.VersionMinor == 1));
        await _processingPipeline.Received(1).EnqueueAsync(document.Id, document.CurrentVersionId, Arg.Any<CancellationToken>());
        await _resumableStorage.Received(1).DeleteAsync(session.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldIncrementMajorVersion_WhenIncrementIsMajor()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _) = SetUpDocument();
        var session = SetUpSession(document.Id);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>()).Returns("stored-v2.pdf");

        await CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Major), CancellationToken.None);

        _documentRepository.Received(1).AddVersion(Arg.Is<DocumentVersion>(v => v != null && v.VersionMajor == 2 && v.VersionMinor == 0));
    }

    [Fact]
    public async Task Handle_ShouldNeverTouchThePriorVersionRow()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, currentVersion) = SetUpDocument();
        var session = SetUpSession(document.Id);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>()).Returns("stored-v2.pdf");

        await CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        currentVersion.StoredFileName.Should().Be("stored-v1.pdf");
        currentVersion.SizeBytes.Should().Be(1000);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenSessionTargetsADifferentDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _) = SetUpDocument();
        var session = SetUpSession(Guid.CreateVersion7()); // Targets some other document.

        var act = () => CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenUploadIsIncomplete()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _) = SetUpDocument();
        var session = SetUpSession(document.Id, declaredSizeBytes: 2000);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(1000L); // Short of declared.

        var act = () => CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenValidationFails()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _) = SetUpDocument();
        var session = SetUpSession(document.Id);
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), session.FileName, Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Invalid("Corrupted file."));

        var act = () => CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>().WithMessage("Corrupted file.");
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var (document, _) = SetUpDocument(); // Owned by user-1.
        var session = SetUpSession(document.Id);

        var act = () => CreateSut().Handle(new ReplaceDocumentCommand(document.Id, session.Id, VersionIncrement.Minor), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
