using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class GetUsersEligibleIdsQueryHandlerTests
{
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GetUsersEligibleIdsQueryHandler _handler;

    public GetUsersEligibleIdsQueryHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new GetUsersEligibleIdsQueryHandler(_userAdminRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_DelegatesToRepositoryWithActorAsExcludedId()
    {
        _userAdminRepository.ListEligibleIdsAsync("ana", UserBulkAction.Lock, "actor-1", Arg.Any<CancellationToken>())
            .Returns(["user-1", "user-2"]);

        var result = await _handler.Handle(new GetUsersEligibleIdsQuery("ana", UserBulkAction.Lock), CancellationToken.None);

        result.Should().Equal("user-1", "user-2");
        await _userAdminRepository.Received(1).ListEligibleIdsAsync("ana", UserBulkAction.Lock, "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoCurrentUser_ThrowsUnauthorized()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => _handler.Handle(new GetUsersEligibleIdsQuery(null, UserBulkAction.Delete), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
