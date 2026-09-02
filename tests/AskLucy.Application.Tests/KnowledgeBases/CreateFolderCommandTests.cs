using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.CreateFolder;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class CreateFolderCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreateFolderCommandHandler CreateHandler(int maxNestingDepth = 10) => new(
        _knowledgeBaseRepository, _folderRepository, Microsoft.Extensions.Options.Options.Create(new KnowledgeBaseFolderOptions { MaxNestingDepth = maxNestingDepth }),
        _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldCreateARootFolder()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var result = await CreateHandler().Handle(new CreateFolderCommand(knowledgeBase.Id, "Contracts", null), CancellationToken.None);

        result.Name.Should().Be("Contracts");
        result.Depth.Should().Be(0);
        _folderRepository.Received(1).Add(Arg.Is<KnowledgeBaseFolder>(f => f != null && f.Name == "Contracts"));
    }

    [Fact]
    public async Task Handle_ShouldRejectNestingPastMaxNestingDepth()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var deepParent = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Deep", null, parentDepth: 0, maxNestingDepth: 10, "user-1");
        // Simulate a parent already at the configured max depth.
        deepParent.MoveTo(Guid.NewGuid(), newParentDepth: 0, maxNestingDepth: 1, "user-1");
        _folderRepository.GetByIdAsync(deepParent.Id, Arg.Any<CancellationToken>()).Returns(deepParent);

        var act = () => CreateHandler(maxNestingDepth: 1).Handle(
            new CreateFolderCommand(knowledgeBase.Id, "TooDeep", deepParent.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenParentFolderDoesNotExist()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _folderRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KnowledgeBaseFolder?)null);

        var act = () => CreateHandler().Handle(new CreateFolderCommand(knowledgeBase.Id, "Orphan", Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
