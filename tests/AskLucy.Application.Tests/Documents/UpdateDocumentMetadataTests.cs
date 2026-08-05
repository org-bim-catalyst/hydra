using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.UpdateDocumentMetadata;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>
/// T079 — <see cref="UpdateDocumentMetadataCommandHandler"/>'s staleness-merge wiring (FR-031a,
/// research.md Decision 9). The actual EF Core reload/retry mechanics live in
/// <c>DocumentRepository.SaveMetadataResolvingStalenessAsync</c> (a real SQL Server concern, not
/// fakeable meaningfully here) — this test verifies the handler applies the edit, passes the
/// client's <c>RowVersion</c> through as the concurrency check, and surfaces whatever
/// <c>wasStale</c> the repository reports rather than swallowing or inverting it.
/// </summary>
public sealed class UpdateDocumentMetadataTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private (Document Document, DocumentMetadata Metadata) SetUp()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        var metadata = DocumentMetadata.CreateFromExtraction(document.Id, "Old Title", "Old Author", null, null, null, null, "system:processing");

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetMetadataByDocumentIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(metadata);

        return (document, metadata);
    }

    private UpdateDocumentMetadataCommandHandler CreateSut() => new(_documentRepository, _currentUser);

    [Fact]
    public async Task Handle_ShouldApplyTheEditAndReturnWasStaleFalse_WhenNoConcurrentEditOccurred()
    {
        var (document, metadata) = SetUp();
        _documentRepository.SaveMetadataResolvingStalenessAsync(metadata, Arg.Any<byte[]>(), Arg.Any<Action<DocumentMetadata>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var command = new UpdateDocumentMetadataCommand(document.Id, [1, 2, 3], "New Title", "New Author", null, null, "kw1, kw2");
        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.WasStale.Should().BeFalse();
        result.Metadata.Title.Should().Be("New Title");
        metadata.Title.Should().Be("New Title");
        metadata.IsAutoExtracted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldReturnWasStaleTrue_WhenTheRepositoryReportsAConcurrentEditWasMerged()
    {
        var (document, metadata) = SetUp();
        _documentRepository.SaveMetadataResolvingStalenessAsync(metadata, Arg.Any<byte[]>(), Arg.Any<Action<DocumentMetadata>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var command = new UpdateDocumentMetadataCommand(document.Id, [1, 2, 3], "New Title", null, null, null, null);
        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.WasStale.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldPassTheClientRowVersion_AsTheConcurrencyCheck()
    {
        var (document, metadata) = SetUp();
        byte[]? capturedRowVersion = null;
        _documentRepository.SaveMetadataResolvingStalenessAsync(metadata, Arg.Do<byte[]>(rv => capturedRowVersion = rv), Arg.Any<Action<DocumentMetadata>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var clientRowVersion = new byte[] { 9, 9, 9 };
        await CreateSut().Handle(new UpdateDocumentMetadataCommand(document.Id, clientRowVersion, "New Title", null, null, null, null), CancellationToken.None);

        capturedRowVersion.Should().BeEquivalentTo(clientRowVersion);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var act = () => CreateSut().Handle(new UpdateDocumentMetadataCommand(document.Id, [], "X", null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenNoMetadataExistsYet()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetMetadataByDocumentIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns((DocumentMetadata?)null);

        var act = () => CreateSut().Handle(new UpdateDocumentMetadataCommand(document.Id, [], "X", null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
