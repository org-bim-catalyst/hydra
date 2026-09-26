using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;
using AskLucy.Application.Users;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class GetRolesEligibleIdsQueryHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GetRolesEligibleIdsQueryHandler _handler;

    public GetRolesEligibleIdsQueryHandlerTests()
    {
        _handler = new GetRolesEligibleIdsQueryHandler(_roleRepository, _currentUser);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_DelegatesToRepository_WithTheActorsSuperUserStatus(bool actorIsSuperUser)
    {
        // specs/074 FR-016j: the repository drops roles carrying View user content for a non-Super-User.
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(actorIsSuperUser);
        _roleRepository.ListEligibleIdsAsync("proj", actorIsSuperUser, Arg.Any<CancellationToken>()).Returns(["role-1", "role-2"]);

        var result = await _handler.Handle(new GetRolesEligibleIdsQuery("proj"), CancellationToken.None);

        result.Should().Equal("role-1", "role-2");
        await _roleRepository.Received(1).ListEligibleIdsAsync("proj", actorIsSuperUser, Arg.Any<CancellationToken>());
    }
}
