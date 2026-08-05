using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.DeleteFolder;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T091 — <c>DeleteFolder</c> on a non-empty folder requires an explicit <c>onContainedDocuments</c> choice (400 if omitted, Edge Cases).</summary>
public sealed class DeleteFolderCommandTests
{
    private readonly IDocumentFolderRepository _folderRepository = Substitute.For<IDocumentFolderRepository>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public DeleteFolderCommandTests() => _currentUser.UserId.Returns("user-1");

    private DeleteFolderCommandHandler CreateSut() => new(_folderRepository, _documentRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldThrow_WhenTheFolderIsNonEmptyAndNoChoiceWasSupplied()
    {
        var folder = DocumentFolder.Create("user-1", "Folder", null, 0, "user-1");
        var containedDocument = Document.Create(Guid.CreateVersion7(), "user-1", "a.pdf", DocumentFileType.Pdf, 1, Guid.CreateVersion7(), "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.ListByFolderIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns([containedDocument]);

        var act = () => CreateSut().Handle(new DeleteFolderCommand(folder.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _folderRepository.DidNotReceive().Remove(Arg.Any<DocumentFolder>());
    }

    [Fact]
    public async Task Handle_ShouldDeleteAnEmptyFolder_WithoutRequiringAChoice()
    {
        var folder = DocumentFolder.Create("user-1", "Empty Folder", null, 0, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.ListByFolderIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns([]);

        await CreateSut().Handle(new DeleteFolderCommand(folder.Id, null), CancellationToken.None);

        _folderRepository.Received(1).Remove(folder);
    }

    [Fact]
    public async Task Handle_ShouldMoveContainedDocumentsToTheParentFolder_WhenChosen()
    {
        var parentFolder = DocumentFolder.Create("user-1", "Parent", null, 0, "user-1");
        var folder = DocumentFolder.Create("user-1", "Folder", parentFolder.Id, 1, "user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "a.pdf", DocumentFileType.Pdf, 1, Guid.CreateVersion7(), "user-1");
        document.MoveToFolder(folder.Id, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.ListByFolderIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns([document]);

        await CreateSut().Handle(new DeleteFolderCommand(folder.Id, OnContainedDocumentsAction.MoveToParent), CancellationToken.None);

        document.FolderId.Should().Be(parentFolder.Id);
    }

    [Fact]
    public async Task Handle_ShouldArchiveContainedDocuments_WhenChosen()
    {
        var folder = DocumentFolder.Create("user-1", "Folder", null, 0, "user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "a.pdf", DocumentFileType.Pdf, 1, Guid.CreateVersion7(), "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.ListByFolderIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns([document]);

        await CreateSut().Handle(new DeleteFolderCommand(folder.Id, OnContainedDocumentsAction.ArchiveAll), CancellationToken.None);

        document.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldSoftDeleteContainedDocuments_WhenChosen()
    {
        var folder = DocumentFolder.Create("user-1", "Folder", null, 0, "user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "a.pdf", DocumentFileType.Pdf, 1, Guid.CreateVersion7(), "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.ListByFolderIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns([document]);

        await CreateSut().Handle(new DeleteFolderCommand(folder.Id, OnContainedDocumentsAction.DeleteAll), CancellationToken.None);

        document.IsDeleted.Should().BeTrue();
    }
}
