using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.DeleteFolder;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class DeleteFolderCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<DeleteFolderCommandHandler> _logger = Substitute.For<ILogger<DeleteFolderCommandHandler>>();

    private DeleteFolderCommandHandler CreateHandler() =>
        new(_knowledgeBaseRepository, _folderRepository, _documentRepository, _unitOfWork, _currentUser, _logger);

    [Fact]
    public async Task Handle_ShouldRejectWithoutConfirmation_WhenFolderIsNonEmpty()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", null, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([folder]);
        _folderRepository.HasContentsAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new DeleteFolderCommand(knowledgeBase.Id, folder.Id, Confirm: false), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldDeleteAnEmptyFolder_WithoutConfirmation()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", null, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([folder]);
        _folderRepository.HasContentsAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(false);
        _documentRepository.ListByFolderAsync(knowledgeBase.Id, folder.Id, Arg.Any<CancellationToken>()).Returns([]);

        await CreateHandler().Handle(new DeleteFolderCommand(knowledgeBase.Id, folder.Id, Confirm: false), CancellationToken.None);

        folder.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldCascadeDeleteDescendantFoldersAndDocuments_WhenConfirmed()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var parent = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Parent", null, 0, 10, "user-1");
        var child = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Child", parent.Id, 0, 10, "user-1");
        var document = KnowledgeBaseDocument.Create(knowledgeBase.Id, child.Id, "a.pdf", "stored-a", "application/pdf", 100, 3, "user-1");
        knowledgeBase.ApplyDocumentAdded(document.PageCount, document.SizeBytes, "user-1");

        _folderRepository.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([parent, child]);
        _documentRepository.ListByFolderAsync(knowledgeBase.Id, parent.Id, Arg.Any<CancellationToken>()).Returns([]);
        _documentRepository.ListByFolderAsync(knowledgeBase.Id, child.Id, Arg.Any<CancellationToken>()).Returns([document]);

        await CreateHandler().Handle(new DeleteFolderCommand(knowledgeBase.Id, parent.Id, Confirm: true), CancellationToken.None);

        parent.IsDeleted.Should().BeTrue();
        child.IsDeleted.Should().BeTrue();
        document.IsDeleted.Should().BeTrue();
        knowledgeBase.DocumentCount.Should().Be(0, "the cascade must decrement the knowledge base's cached statistics for every removed document");
    }
}
