using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.DeleteCategory;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class DeleteCategoryCommandTests
{
    private readonly IKnowledgeBaseCategoryRepository _categoryRepository = Substitute.For<IKnowledgeBaseCategoryRepository>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DeleteCategoryCommandHandler CreateHandler() => new(_categoryRepository, _knowledgeBaseRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldClearCategoryId_OnEveryReferencingKnowledgeBase_AndRemoveTheCategory()
    {
        _currentUser.UserId.Returns("user-1");
        var category = KnowledgeBaseCategory.CreateCustom("Vendor Docs", "user-1", "user-1");
        _categoryRepository.GetByIdAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        var knowledgeBaseOne = KnowledgeBase.Create("KB One", "user-1", "user-1");
        knowledgeBaseOne.UpdateDetails("KB One", null, null, null, category.Id, null, "user-1");
        var knowledgeBaseTwo = KnowledgeBase.Create("KB Two", "user-1", "user-1");
        knowledgeBaseTwo.UpdateDetails("KB Two", null, null, null, category.Id, null, "user-1");
        _knowledgeBaseRepository.ListByCategoryIdAsync(category.Id, Arg.Any<CancellationToken>())
            .Returns([knowledgeBaseOne, knowledgeBaseTwo]);

        await CreateHandler().Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        knowledgeBaseOne.CategoryId.Should().BeNull();
        knowledgeBaseTwo.CategoryId.Should().BeNull();
        _categoryRepository.Received(1).Remove(category);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectDeletingAPredefinedCategory()
    {
        _currentUser.UserId.Returns("user-1");
        // The domain deliberately has no public factory for a predefined (OwnerId == null)
        // category — the 8 predefined categories only ever come from the migration's seed
        // data (constitution §5) — so the private setter is reached via reflection here.
        var predefined = KnowledgeBaseCategory.CreateCustom("Engineering", "user-1", "user-1");
        typeof(KnowledgeBaseCategory).GetProperty(nameof(KnowledgeBaseCategory.OwnerId))!.GetSetMethod(nonPublic: true)!.Invoke(predefined, [null]);
        _categoryRepository.GetByIdAsync(predefined.Id, Arg.Any<CancellationToken>()).Returns(predefined);

        var act = () => CreateHandler().Handle(new DeleteCategoryCommand(predefined.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _categoryRepository.DidNotReceive().Remove(Arg.Any<KnowledgeBaseCategory>());
    }

    [Fact]
    public async Task Handle_ShouldRejectDeletingAnotherOwnersCategory()
    {
        _currentUser.UserId.Returns("user-1");
        var othersCategory = KnowledgeBaseCategory.CreateCustom("Their Category", "user-2", "user-2");
        _categoryRepository.GetByIdAsync(othersCategory.Id, Arg.Any<CancellationToken>()).Returns(othersCategory);

        var act = () => CreateHandler().Handle(new DeleteCategoryCommand(othersCategory.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldRejectANonExistentCategory()
    {
        _currentUser.UserId.Returns("user-1");
        _categoryRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KnowledgeBaseCategory?)null);

        var act = () => CreateHandler().Handle(new DeleteCategoryCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
