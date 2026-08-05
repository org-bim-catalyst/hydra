using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Queries.GetVersionTimeline;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T106 — <c>GetVersionTimeline</c> ordering and creator/date fields (FR-040).</summary>
public sealed class VersionTimelineTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private GetVersionTimelineQueryHandler CreateSut() => new(_documentRepository, _currentUser);

    [Fact]
    public async Task Handle_ShouldReturnEveryVersion_WithLabelAndIsCurrentFlagSetCorrectly()
    {
        _currentUser.UserId.Returns("user-1");
        var documentId = Guid.CreateVersion7();
        var v1 = DocumentVersion.Create(documentId, 1, 0, "v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "alice");
        var v2 = DocumentVersion.Create(documentId, 2, 0, "v2.pdf", "report.pdf", 1500, Guid.CreateVersion7(), "bob");
        var document = Document.Create(documentId, "user-1", "report.pdf", DocumentFileType.Pdf, 1500, v2.Id, "user-1");

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetVersionsByDocumentIdAsync(document.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DocumentVersion>)[v2, v1]); // Newest-first, as the repository contract promises.

        var result = await CreateSut().Handle(new GetVersionTimelineQuery(document.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(v2.Id);
        result[0].VersionLabel.Should().Be("2.0");
        result[0].CreatedByUserId.Should().Be("bob");
        result[0].IsCurrent.Should().BeTrue();
        result[1].Id.Should().Be(v1.Id);
        result[1].VersionLabel.Should().Be("1.0");
        result[1].CreatedByUserId.Should().Be("alice");
        result[1].IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var documentId = Guid.CreateVersion7();
        var v1 = DocumentVersion.Create(documentId, 1, 0, "v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(documentId, "user-1", "report.pdf", DocumentFileType.Pdf, 1000, v1.Id, "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var act = () => CreateSut().Handle(new GetVersionTimelineQuery(document.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
