using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Queries.ExportKnowledgeBase;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class ExportKnowledgeBaseQueryTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IKnowledgeBaseCategoryRepository _categoryRepository = Substitute.For<IKnowledgeBaseCategoryRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-08-04T12:00:00Z"));

    private ExportKnowledgeBaseQueryHandler CreateHandler() =>
        new(_knowledgeBaseRepository, _folderRepository, _categoryRepository, _timeProvider, _currentUser);

    [Fact]
    public async Task Handle_ShouldProduceTheDocumentedShape_ForAKnowledgeBaseWithZeroDocuments()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("BIM Standards", "user-1", "user-1");
        knowledgeBase.AddTag("revit", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(new ExportKnowledgeBaseQuery(knowledgeBase.Id), CancellationToken.None);

        result.Id.Should().Be(knowledgeBase.Id);
        result.Name.Should().Be("BIM Standards");
        result.Tags.Should().ContainSingle().Which.Should().Be("revit");
        result.Folders.Should().BeEmpty();
        result.DocumentCount.Should().Be(0);
        result.ExportedAtUtc.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Handle_ShouldIncludeTheFolderStructure_AsAFlatNameAndHierarchyList()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("BIM Standards", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var root = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Drawings", null, 0, 10, "user-1");
        var child = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Revit", root.Id, root.Depth, 10, "user-1");
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([root, child]);

        var result = await CreateHandler().Handle(new ExportKnowledgeBaseQuery(knowledgeBase.Id), CancellationToken.None);

        result.Folders.Should().HaveCount(2);
        result.Folders.Should().Contain(f => f.Id == child.Id && f.ParentFolderId == root.Id);
    }

    [Fact]
    public async Task Handle_ShouldResolveTheCategoryName_WhenACategoryIsAssigned()
    {
        _currentUser.UserId.Returns("user-1");
        var category = KnowledgeBaseCategory.CreateCustom("Engineering", "user-1", "user-1");
        var knowledgeBase = KnowledgeBase.Create("BIM Standards", "user-1", "user-1");
        knowledgeBase.UpdateDetails("BIM Standards", null, null, null, category.Id, null, "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([]);
        _categoryRepository.GetByIdAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await CreateHandler().Handle(new ExportKnowledgeBaseQuery(knowledgeBase.Id), CancellationToken.None);

        result.CategoryId.Should().Be(category.Id);
        result.CategoryName.Should().Be("Engineering");
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenNotOwnedByTheCaller()
    {
        _currentUser.UserId.Returns("user-1");
        var othersKnowledgeBase = KnowledgeBase.Create("Theirs", "user-2", "user-2");
        _knowledgeBaseRepository.GetByIdAsync(othersKnowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(othersKnowledgeBase);

        var act = () => CreateHandler().Handle(new ExportKnowledgeBaseQuery(othersKnowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
