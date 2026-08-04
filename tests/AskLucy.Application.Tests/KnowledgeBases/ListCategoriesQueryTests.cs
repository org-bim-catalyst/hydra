using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Queries.ListCategories;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class ListCategoriesQueryTests
{
    private readonly IKnowledgeBaseCategoryRepository _repository = Substitute.For<IKnowledgeBaseCategoryRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldReturnPredefinedAndOwnedCategories_MappedToDtos()
    {
        _currentUser.UserId.Returns("user-1");
        var another = KnowledgeBaseCategory.CreateCustom("Engineering Notes", "user-1", "user-1");
        var custom = KnowledgeBaseCategory.CreateCustom("Vendor Docs", "user-1", "user-1");
        _repository.ListPredefinedAndOwnedAsync("user-1", Arg.Any<CancellationToken>()).Returns([another, custom]);
        var handler = new ListCategoriesQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new ListCategoriesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(c => c.Id == custom.Id && c.Name == "Vendor Docs" && !c.IsPredefined);
    }

    [Fact]
    public async Task Handle_ShouldScopeToTheCallingUser()
    {
        _currentUser.UserId.Returns("user-1");
        _repository.ListPredefinedAndOwnedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        var handler = new ListCategoriesQueryHandler(_repository, _currentUser);

        await handler.Handle(new ListCategoriesQuery(), CancellationToken.None);

        await _repository.Received(1).ListPredefinedAndOwnedAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var handler = new ListCategoriesQueryHandler(_repository, _currentUser);

        var act = () => handler.Handle(new ListCategoriesQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
