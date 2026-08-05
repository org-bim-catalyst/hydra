using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.ArchiveDocument;
using AskLucy.Application.Documents.Commands.DeleteDocument;
using AskLucy.Application.Documents.Commands.RenameDocument;
using AskLucy.Application.Documents.Commands.RestoreDocument;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

public sealed class DocumentLifecycleCommandTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private static Document CreateOwnedDocument(string ownerId = "user-1") =>
        Document.Create(Guid.CreateVersion7(), ownerId, "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), ownerId);

    [Fact]
    public async Task Rename_ShouldUpdateFileName()
    {
        _currentUser.UserId.Returns("user-1");
        var document = CreateOwnedDocument();
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new RenameDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new RenameDocumentCommand(document.Id, "renamed.pdf"), CancellationToken.None);

        result.FileName.Should().Be("renamed.pdf");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var document = CreateOwnedDocument("user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new RenameDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new RenameDocumentCommand(document.Id, "renamed.pdf"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Archive_ShouldSetArchivedAtUtc()
    {
        _currentUser.UserId.Returns("user-1");
        var document = CreateOwnedDocument();
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new ArchiveDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new ArchiveDocumentCommand(document.Id), CancellationToken.None);

        document.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_ShouldUndoArchive_UsingGetByIdIncludingDeleted()
    {
        _currentUser.UserId.Returns("user-1");
        var document = CreateOwnedDocument();
        document.Archive("user-1");
        _documentRepository.GetByIdIncludingDeletedAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new RestoreDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new RestoreDocumentCommand(document.Id), CancellationToken.None);

        document.ArchivedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ShouldSoftDelete_NotHardDelete()
    {
        _currentUser.UserId.Returns("user-1");
        var document = CreateOwnedDocument();
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new DeleteDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new DeleteDocumentCommand(document.Id), CancellationToken.None);

        document.IsDeleted.Should().BeTrue();
        document.DeletedBy.Should().Be("user-1");
    }

    [Fact]
    public async Task Delete_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var document = CreateOwnedDocument("user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var handler = new DeleteDocumentCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new DeleteDocumentCommand(document.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
