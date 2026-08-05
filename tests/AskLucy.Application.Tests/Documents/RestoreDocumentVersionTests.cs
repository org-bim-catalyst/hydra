using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.RestoreDocumentVersion;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T104 — <c>RestoreDocumentVersion</c> repoints without deleting history; returns `409 VersionUploadInProgress` while a replacement upload is in flight for the same document (Edge Cases).</summary>
public sealed class RestoreDocumentVersionTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RestoreDocumentVersionCommandHandler CreateSut() => new(_documentRepository, _sessionRepository, _unitOfWork, _currentUser);

    private (Document Document, DocumentVersion CurrentVersion, DocumentVersion PriorVersion) SetUpDocumentWithTwoVersions()
    {
        var priorVersion = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "stored-v1.pdf", "report.pdf", 1000, Guid.CreateVersion7(), "user-1");
        var currentVersion = DocumentVersion.Create(priorVersion.DocumentId, 2, 0, "stored-v2.pdf", "report.pdf", 2000, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(priorVersion.DocumentId, "user-1", "report.pdf", DocumentFileType.Pdf, 2000, currentVersion.Id, "user-1");

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetVersionByIdAsync(priorVersion.Id, Arg.Any<CancellationToken>()).Returns(priorVersion);
        _documentRepository.GetVersionByIdAsync(currentVersion.Id, Arg.Any<CancellationToken>()).Returns(currentVersion);
        _sessionRepository.GetInProgressForDocumentAsync(document.Id, Arg.Any<CancellationToken>()).Returns((DocumentUploadSession?)null);

        return (document, currentVersion, priorVersion);
    }

    [Fact]
    public async Task Handle_ShouldRepointCurrentVersionId_WithoutDeletingAnyVersion()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _, priorVersion) = SetUpDocumentWithTwoVersions();

        var result = await CreateSut().Handle(new RestoreDocumentVersionCommand(document.Id, priorVersion.Id), CancellationToken.None);

        document.CurrentVersionId.Should().Be(priorVersion.Id);
        result.Id.Should().Be(document.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowConflict_WhenAReplacementUploadIsInProgressForThisDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _, priorVersion) = SetUpDocumentWithTwoVersions();
        var inFlightSession = DocumentUploadSession.Create("user-1", "new.pdf", 500, 256, DateTime.UtcNow.AddHours(1), "user-1", document.Id);
        _sessionRepository.GetInProgressForDocumentAsync(document.Id, Arg.Any<CancellationToken>()).Returns(inFlightSession);

        var act = () => CreateSut().Handle(new RestoreDocumentVersionCommand(document.Id, priorVersion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<VersionUploadInProgressException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenVersionBelongsToAnotherDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var (document, _, _) = SetUpDocumentWithTwoVersions();
        var foreignVersion = DocumentVersion.Create(Guid.CreateVersion7(), 1, 0, "stored.pdf", "other.pdf", 500, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetVersionByIdAsync(foreignVersion.Id, Arg.Any<CancellationToken>()).Returns(foreignVersion);

        var act = () => CreateSut().Handle(new RestoreDocumentVersionCommand(document.Id, foreignVersion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var (document, _, priorVersion) = SetUpDocumentWithTwoVersions(); // Owned by user-1.

        var act = () => CreateSut().Handle(new RestoreDocumentVersionCommand(document.Id, priorVersion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
