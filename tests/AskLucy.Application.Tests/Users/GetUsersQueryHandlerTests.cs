using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Queries.GetUsers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class GetUsersQueryHandlerTests
{
    private readonly IUserAdminRepository _repository = Substitute.For<IUserAdminRepository>();
    private readonly GetUsersQueryHandler _handler;

    public GetUsersQueryHandlerTests() => _handler = new GetUsersQueryHandler(_repository);

    [Fact]
    public async Task Handle_ShouldPassParametersThrough_AndReturnRepositoryResult()
    {
        var expected = new PagedResult<UserAdminDto>(
            [new UserAdminDto("u1", "a@example.com", "A", null, true, false, false, false, "Regular", DateTime.UtcNow)],
            TotalCount: 1, Page: 2, PageSize: 10);

        _repository.SearchAsync("jane", "createdAtUtc", true, 2, 10, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _handler.Handle(new GetUsersQuery("jane", "createdAtUtc", true, 2, 10), CancellationToken.None);

        result.Should().Be(expected);
    }
}
