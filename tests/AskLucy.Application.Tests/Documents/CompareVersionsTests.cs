using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Queries.CompareVersions;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T105 — <c>CompareVersions</c> diffs extracted text and metadata (FR-042).</summary>
public sealed class CompareVersionsTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CompareVersionsQueryHandler CreateSut() => new(_documentRepository, _currentUser);

    private Document SetUpDocument(DocumentVersion current)
    {
        var document = Document.Create(current.DocumentId, "user-1", "report.pdf", DocumentFileType.Pdf, current.SizeBytes, current.Id, "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        return document;
    }

    [Fact]
    public async Task Handle_ShouldDiffExtractedText_LineByLine()
    {
        _currentUser.UserId.Returns("user-1");
        var documentId = Guid.CreateVersion7();
        var fromVersion = DocumentVersion.Create(documentId, 1, 0, "v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        fromVersion.ApplyExtractedText("line one\nline two\nline three", null, 1, "system");
        var toVersion = DocumentVersion.Create(documentId, 2, 0, "v2.pdf", "report.pdf", 1200, Guid.CreateVersion7(), "user-1");
        toVersion.ApplyExtractedText("line one\nline two changed\nline three", null, 1, "system");

        var document = SetUpDocument(toVersion);
        _documentRepository.GetVersionByIdAsync(fromVersion.Id, Arg.Any<CancellationToken>()).Returns(fromVersion);
        _documentRepository.GetVersionByIdAsync(toVersion.Id, Arg.Any<CancellationToken>()).Returns(toVersion);

        var result = await CreateSut().Handle(new CompareVersionsQuery(document.Id, fromVersion.Id, toVersion.Id), CancellationToken.None);

        result.ExtractedTextDiff.Should().Contain("line one");
        result.ExtractedTextDiff.Should().Contain("- line two");
        result.ExtractedTextDiff.Should().Contain("+ line two changed");
        result.ExtractedTextDiff.Should().Contain("line three");
    }

    [Fact]
    public async Task Handle_ShouldIncludeOnlyFieldsThatActuallyDiffer_InMetadataDiff()
    {
        _currentUser.UserId.Returns("user-1");
        var documentId = Guid.CreateVersion7();
        var fromVersion = DocumentVersion.Create(documentId, 1, 0, "v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        fromVersion.ApplyExtractedText("text", null, 3, "system");
        var toVersion = DocumentVersion.Create(documentId, 2, 0, "v2.pdf", "report.pdf", 1500, Guid.CreateVersion7(), "user-1");
        toVersion.ApplyExtractedText("text", null, 5, "system");

        var document = SetUpDocument(toVersion);
        _documentRepository.GetVersionByIdAsync(fromVersion.Id, Arg.Any<CancellationToken>()).Returns(fromVersion);
        _documentRepository.GetVersionByIdAsync(toVersion.Id, Arg.Any<CancellationToken>()).Returns(toVersion);

        var result = await CreateSut().Handle(new CompareVersionsQuery(document.Id, fromVersion.Id, toVersion.Id), CancellationToken.None);

        result.MetadataDiff.Should().ContainKey("sizeBytes").WhoseValue.Should().Be(new MetadataFieldDiff("1000", "1500"));
        result.MetadataDiff.Should().ContainKey("pageCount").WhoseValue.Should().Be(new MetadataFieldDiff("3", "5"));
        result.MetadataDiff.Should().NotContainKey("originalFileName"); // Identical on both versions — never included.
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenAVersionBelongsToAnotherDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var documentId = Guid.CreateVersion7();
        var fromVersion = DocumentVersion.Create(documentId, 1, 0, "v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        var foreignVersion = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "other.pdf", "other.pdf", 500, Guid.CreateVersion7(), "user-1");

        var document = SetUpDocument(fromVersion);
        _documentRepository.GetVersionByIdAsync(fromVersion.Id, Arg.Any<CancellationToken>()).Returns(fromVersion);
        _documentRepository.GetVersionByIdAsync(foreignVersion.Id, Arg.Any<CancellationToken>()).Returns(foreignVersion);

        var act = () => CreateSut().Handle(new CompareVersionsQuery(document.Id, fromVersion.Id, foreignVersion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
