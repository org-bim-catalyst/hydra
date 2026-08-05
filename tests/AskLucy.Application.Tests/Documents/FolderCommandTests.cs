using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.CreateFolder;
using AskLucy.Application.Documents.Commands.MoveFolder;
using AskLucy.Application.Documents.Commands.RenameFolder;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T090 — <c>CreateFolder</c>/<c>RenameFolder</c>/<c>MoveFolder</c>, including rejecting a move into itself or a descendant (FR-033, Edge Cases).</summary>
public sealed class FolderCommandTests
{
    private readonly IDocumentFolderRepository _folderRepository = Substitute.For<IDocumentFolderRepository>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public FolderCommandTests() => _currentUser.UserId.Returns("user-1");

    [Fact]
    public async Task CreateFolder_ShouldCreateARootFolder_WhenNoParentIsSupplied()
    {
        var handler = new CreateFolderCommandHandler(_folderRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new CreateFolderCommand("Invoices", null), CancellationToken.None);

        result.Name.Should().Be("Invoices");
        result.Depth.Should().Be(0);
        result.ParentFolderId.Should().BeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFolder_ShouldComputeDepthFromTheParent()
    {
        var parent = DocumentFolder.Create("user-1", "Parent", null, 0, "user-1");
        _folderRepository.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);

        var handler = new CreateFolderCommandHandler(_folderRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new CreateFolderCommand("Child", parent.Id), CancellationToken.None);

        result.Depth.Should().Be(1);
        result.ParentFolderId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task RenameFolder_ShouldUpdateTheName()
    {
        var folder = DocumentFolder.Create("user-1", "Old Name", null, 0, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _documentRepository.CountDocumentsInFolderAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(3);

        var handler = new RenameFolderCommandHandler(_folderRepository, _documentRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new RenameFolderCommand(folder.Id, "New Name"), CancellationToken.None);

        result.Name.Should().Be("New Name");
        result.DocumentCount.Should().Be(3);
    }

    [Fact]
    public async Task RenameFolder_ShouldThrowNotFound_WhenCallerDoesNotOwnTheFolder()
    {
        var folder = DocumentFolder.Create("user-2", "Someone Else's", null, 0, "user-2");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);

        var handler = new RenameFolderCommandHandler(_folderRepository, _documentRepository, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new RenameFolderCommand(folder.Id, "New Name"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MoveFolder_ShouldRepointTheParentAndRecomputeDepth()
    {
        var folder = DocumentFolder.Create("user-1", "Folder", null, 0, "user-1");
        var newParent = DocumentFolder.Create("user-1", "New Parent", null, 0, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.GetByIdAsync(newParent.Id, Arg.Any<CancellationToken>()).Returns(newParent);
        _folderRepository.IsSelfOrDescendantAsync(folder.Id, newParent.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new MoveFolderCommandHandler(_folderRepository, _documentRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new MoveFolderCommand(folder.Id, newParent.Id), CancellationToken.None);

        result.ParentFolderId.Should().Be(newParent.Id);
        result.Depth.Should().Be(1);
    }

    [Fact]
    public async Task MoveFolder_ShouldRejectMovingAFolderIntoItself()
    {
        var folder = DocumentFolder.Create("user-1", "Folder", null, 0, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.IsSelfOrDescendantAsync(folder.Id, folder.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new MoveFolderCommandHandler(_folderRepository, _documentRepository, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new MoveFolderCommand(folder.Id, folder.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task MoveFolder_ShouldRejectMovingAFolderIntoItsOwnDescendant()
    {
        var folder = DocumentFolder.Create("user-1", "Parent", null, 0, "user-1");
        var descendant = DocumentFolder.Create("user-1", "Child", folder.Id, 1, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.GetByIdAsync(descendant.Id, Arg.Any<CancellationToken>()).Returns(descendant);
        _folderRepository.IsSelfOrDescendantAsync(folder.Id, descendant.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new MoveFolderCommandHandler(_folderRepository, _documentRepository, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new MoveFolderCommand(folder.Id, descendant.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
