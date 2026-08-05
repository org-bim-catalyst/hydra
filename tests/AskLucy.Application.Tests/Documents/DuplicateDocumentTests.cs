using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.DuplicateDocument;
using AskLucy.Application.Documents.Processing;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T092 — <c>DuplicateDocument</c> produces an independent copy: own file, metadata, tags; fresh processing history (FR-034).</summary>
public sealed class DuplicateDocumentTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentProcessingPipeline _processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DuplicateDocumentCommandHandler CreateSut() => new(_documentRepository, _fileStorage, _processingPipeline, _unitOfWork, _currentUser);

    private (Document Source, DocumentVersion Version) SetUpSourceDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var versionId = Guid.CreateVersion7();
        var source = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 2048, versionId, "user-1");
        var version = DocumentVersion.Create(source.Id, 1, 0, "stored-original.bin", "report.pdf", 2048, Guid.CreateVersion7(), "user-1");

        _documentRepository.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _documentRepository.GetVersionByIdAsync(versionId, Arg.Any<CancellationToken>()).Returns(version);
        _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream([1, 2, 3]));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), version.OriginalFileName, Arg.Any<CancellationToken>()).Returns("stored-copy.bin");
        _documentRepository.GetChecksumHashAsync(version.ChecksumId, Arg.Any<CancellationToken>()).Returns(new string('a', 64));

        return (source, version);
    }

    [Fact]
    public async Task Handle_ShouldCreateANewDocumentWithItsOwnStoredFile()
    {
        var (source, _) = SetUpSourceDocument();

        Document? capturedDocument = null;
        _documentRepository.When(r => r.Add(Arg.Any<Document>())).Do(c => capturedDocument = c.Arg<Document>());
        DocumentVersion? capturedVersion = null;
        _documentRepository.When(r => r.AddVersion(Arg.Any<DocumentVersion>())).Do(c => capturedVersion = c.Arg<DocumentVersion>());

        var result = await CreateSut().Handle(new DuplicateDocumentCommand(source.Id), CancellationToken.None);

        capturedDocument.Should().NotBeNull();
        capturedDocument!.Id.Should().NotBe(source.Id);
        capturedDocument.FileName.Should().Be(source.FileName);
        capturedVersion!.StoredFileName.Should().Be("stored-copy.bin");
        result.Id.Should().Be(capturedDocument.Id);
        await _fileStorage.Received(1).SaveAsync(Arg.Any<Stream>(), "report.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCopyMetadataAndClassificationIndependently()
    {
        var (source, _) = SetUpSourceDocument();
        var sourceMetadata = DocumentMetadata.CreateFromExtraction(source.Id, "Title", "Author", null, null, "kw", null, "system:processing");
        var category = DocumentCategory.Create("Legal", true, "system");
        var sourceClassification = DocumentClassification.CreateAutomatic(source.Id, category.Id, 0.8m, "system:processing");
        _documentRepository.GetMetadataByDocumentIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(sourceMetadata);
        _documentRepository.GetClassificationByDocumentIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(sourceClassification);

        DocumentMetadata? capturedMetadata = null;
        _documentRepository.When(r => r.AddMetadata(Arg.Any<DocumentMetadata>())).Do(c => capturedMetadata = c.Arg<DocumentMetadata>());
        DocumentClassification? capturedClassification = null;
        _documentRepository.When(r => r.AddClassification(Arg.Any<DocumentClassification>())).Do(c => capturedClassification = c.Arg<DocumentClassification>());

        var result = await CreateSut().Handle(new DuplicateDocumentCommand(source.Id), CancellationToken.None);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.Id.Should().NotBe(sourceMetadata.Id);
        capturedMetadata.DocumentId.Should().Be(result.Id);
        capturedMetadata.Title.Should().Be("Title");
        capturedClassification!.DocumentId.Should().Be(result.Id);
        capturedClassification.CategoryId.Should().Be(category.Id);
    }

    [Fact]
    public async Task Handle_ShouldCopyTags()
    {
        var (source, _) = SetUpSourceDocument();
        var tag = DocumentTag.Create("user-1", "Reviewed", "user-1");
        source.AddTag(tag, "user-1");

        var result = await CreateSut().Handle(new DuplicateDocumentCommand(source.Id), CancellationToken.None);

        result.Tags.Should().Contain("Reviewed");
    }

    [Fact]
    public async Task Handle_ShouldEnqueueFreshProcessing_ForTheNewVersion()
    {
        var (source, _) = SetUpSourceDocument();

        var result = await CreateSut().Handle(new DuplicateDocumentCommand(source.Id), CancellationToken.None);

        await _processingPipeline.Received(1).EnqueueAsync(result.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnTheSourceDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var source = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);

        var act = () => CreateSut().Handle(new DuplicateDocumentCommand(source.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
