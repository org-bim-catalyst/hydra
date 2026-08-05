using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Queries.GetDocumentPreview;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T129 — <c>GetDocumentPreview</c> returns the right `previewType` per file type and `Unavailable` for unsupported/not-yet-processed types, never an error (FR-044).</summary>
public sealed class DocumentPreviewTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private GetDocumentPreviewQueryHandler CreateSut() => new(_documentRepository, _currentUser);

    private (Document Document, DocumentVersion Version) SetUpDocument(DocumentFileType fileType)
    {
        _currentUser.UserId.Returns("user-1");
        var version = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "stored.bin", "file.bin", 1024, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(version.DocumentId, "user-1", "file.bin", fileType, 1024, version.Id, "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        return (document, version);
    }

    [Fact]
    public async Task Handle_ShouldReturnPageImage_ForACompletedPdfWithARenderedPage()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Pdf);
        var preview = DocumentPreview.Create(version.Id, DocumentPreviewType.PageImage, "preview-page1.png", 1, "system");
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([preview]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.PreviewType.Should().Be(DocumentPreviewKind.PageImage);
        result.PreviewId.Should().Be(preview.Id);
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnTheFirstPage_WhenMultiplePageImagesExist()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Pdf);
        var page2 = DocumentPreview.Create(version.Id, DocumentPreviewType.PageImage, "preview-page2.png", 2, "system");
        var page1 = DocumentPreview.Create(version.Id, DocumentPreviewType.PageImage, "preview-page1.png", 1, "system");
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([page2, page1]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.PreviewId.Should().Be(page1.Id);
    }

    [Fact]
    public async Task Handle_ShouldReturnThumbnail_ForACompletedImage()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Png);
        var preview = DocumentPreview.Create(version.Id, DocumentPreviewType.Thumbnail, "thumb.png", null, "system");
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([preview]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.PreviewType.Should().Be(DocumentPreviewKind.Thumbnail);
        result.PreviewId.Should().Be(preview.Id);
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredContent_ForAnOfficeDocument_UsingTheExtractedStructureJson()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Word);
        version.ApplyExtractedText("plain text", "{\"headings\":[\"Intro\"]}", null, "system");
        var preview = DocumentPreview.Create(version.Id, DocumentPreviewType.StructuredContent, null, null, "system");
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([preview]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.PreviewType.Should().Be(DocumentPreviewKind.StructuredContent);
        result.PreviewId.Should().BeNull();
        result.StructuredContent.Should().Be("{\"headings\":[\"Intro\"]}");
    }

    [Fact]
    public async Task Handle_ShouldReturnStructuredContent_ForMarkdown_UsingTheExtractedPlainText_WithNoPreviewRowNeeded()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Markdown);
        version.ApplyExtractedText("# Heading\n\nBody text.", null, null, "system");

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.PreviewType.Should().Be(DocumentPreviewKind.StructuredContent);
        result.StructuredContent.Should().Be("# Heading\n\nBody text.");
        await _documentRepository.DidNotReceive().GetPreviewsByVersionIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnUnavailable_ForMarkdown_WhenNotYetProcessed()
    {
        var (document, _) = SetUpDocument(DocumentFileType.Markdown); // ExtractedText still null.

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.Should().Be(DocumentPreviewResultDto.Unavailable);
    }

    [Fact]
    public async Task Handle_ShouldReturnUnavailable_WhenNoPreviewExistsYet_NeverAnError()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Pdf);
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.Should().Be(DocumentPreviewResultDto.Unavailable);
    }

    [Fact]
    public async Task Handle_ShouldReturnUnavailable_ForAnUnsupportedFileTypeWithNoPreviewArtifact()
    {
        var (document, version) = SetUpDocument(DocumentFileType.Json); // No preview generator handles this format.
        _documentRepository.GetPreviewsByVersionIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        result.Should().Be(DocumentPreviewResultDto.Unavailable);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        var (document, _) = SetUpDocument(DocumentFileType.Pdf);
        _currentUser.UserId.Returns("user-2");

        var act = () => CreateSut().Handle(new GetDocumentPreviewQuery(document.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
