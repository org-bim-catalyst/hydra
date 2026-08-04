using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.MoveFolder;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class MoveFolderCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private MoveFolderCommandHandler CreateHandler() => new(
        _knowledgeBaseRepository, _folderRepository, Microsoft.Extensions.Options.Options.Create(new KnowledgeBaseFolderOptions()), _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldRejectMovingAFolderIntoItself()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", null, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.IsSameOrDescendantAsync(folder.Id, folder.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new MoveFolderCommand(knowledgeBase.Id, folder.Id, folder.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldRejectMovingAFolderIntoItsOwnDescendant()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var parent = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Parent", null, 0, 10, "user-1");
        var child = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Child", parent.Id, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);
        _folderRepository.IsSameOrDescendantAsync(child.Id, parent.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new MoveFolderCommand(knowledgeBase.Id, parent.Id, child.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldMoveToANewValidParent()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", null, 0, 10, "user-1");
        var newParent = KnowledgeBaseFolder.Create(knowledgeBase.Id, "NewParent", null, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.GetByIdAsync(newParent.Id, Arg.Any<CancellationToken>()).Returns(newParent);
        _folderRepository.IsSameOrDescendantAsync(newParent.Id, folder.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(new MoveFolderCommand(knowledgeBase.Id, folder.Id, newParent.Id), CancellationToken.None);

        result.ParentFolderId.Should().Be(newParent.Id);
        result.Depth.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ShouldMoveToRoot_WhenNewParentIsNull()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var parent = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Parent", null, 0, 10, "user-1");
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", parent.Id, 1, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);

        var result = await CreateHandler().Handle(new MoveFolderCommand(knowledgeBase.Id, folder.Id, null), CancellationToken.None);

        result.ParentFolderId.Should().BeNull();
        result.Depth.Should().Be(0);
    }
}
