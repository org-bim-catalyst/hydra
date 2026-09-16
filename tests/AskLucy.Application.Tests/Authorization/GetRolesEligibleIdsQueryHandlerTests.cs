using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class GetRolesEligibleIdsQueryHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly GetRolesEligibleIdsQueryHandler _handler;

    public GetRolesEligibleIdsQueryHandlerTests()
    {
        _handler = new GetRolesEligibleIdsQueryHandler(_roleRepository);
    }

    [Fact]
    public async Task Handle_DelegatesToRepository_OnlyCustomRoles()
    {
        _roleRepository.ListEligibleIdsAsync("proj", Arg.Any<CancellationToken>()).Returns(["role-1", "role-2"]);

        var result = await _handler.Handle(new GetRolesEligibleIdsQuery("proj"), CancellationToken.None);

        result.Should().Equal("role-1", "role-2");
        await _roleRepository.Received(1).ListEligibleIdsAsync("proj", Arg.Any<CancellationToken>());
    }
}
