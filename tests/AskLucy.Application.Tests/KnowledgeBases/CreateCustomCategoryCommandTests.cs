using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.CreateCustomCategory;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class CreateCustomCategoryCommandTests
{
    private readonly IKnowledgeBaseCategoryRepository _repository = Substitute.For<IKnowledgeBaseCategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreateCustomCategoryCommandHandler CreateHandler() => new(_repository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldCreateACategoryOwnedByTheCaller()
    {
        _currentUser.UserId.Returns("user-1");
        _repository.ExistsByNameForOwnerAsync("user-1", "Vendor Docs", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(new CreateCustomCategoryCommand("Vendor Docs"), CancellationToken.None);

        result.Name.Should().Be("Vendor Docs");
        result.IsPredefined.Should().BeFalse();
        _repository.Received(1).Add(Arg.Is<KnowledgeBaseCategory>(c => c != null && c.OwnerId == "user-1" && c.Name == "Vendor Docs"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectADuplicateName_CaseInsensitive_ForTheSameOwner()
    {
        _currentUser.UserId.Returns("user-1");
        _repository.ExistsByNameForOwnerAsync("user-1", "vendor docs", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new CreateCustomCategoryCommand("vendor docs"), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateResourceException>();
        _repository.DidNotReceive().Add(Arg.Any<KnowledgeBaseCategory>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new CreateCustomCategoryCommand("Vendor Docs"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
